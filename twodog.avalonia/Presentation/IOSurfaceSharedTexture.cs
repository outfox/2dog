using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Godot;

namespace twodog.Presentation;

/// <summary>
/// macOS sharing flavor: the engine allocates an IOSurface-backed texture and the compositor
/// imports the IOSurfaceRef. Avalonia's Metal compositor presents imported images only
/// through timeline semaphores (MTLSharedEvent; automatic sync throws), and the engine exports
/// no event of its own - so each buffer carries a host-created shared event: the host signals
/// it once the engine's copy is done (the present call stalls until the GPU finished), and the
/// compositor signals it back after sampling, which gates the buffer's next acquisition.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class IOSurfaceSharedTextureFactory(ICompositionGpuInterop interop) : ISharedTextureFactory
{
    private nint _device;

    public string AvaloniaHandleType => KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef;

    public CompositionGpuImportedImageSynchronizationCapabilities RequiredSynchronization =>
        CompositionGpuImportedImageSynchronizationCapabilities.TimelineSemaphores;

    // Two would do with the event round trip in place; three keeps a spare while a buffer
    // waits for the compositor's signal, so the writer never idles on it.
    public int BufferCount => 3;

    public ISharedTexture Create(RenderingDevice rd, int width, int height)
    {
        if (_device == 0)
        {
            _device = Metal.MTLCreateSystemDefaultDevice();
            if (_device == 0) throw new PlatformNotSupportedException("no Metal device for shared events");
        }
        var rid = rd.ExternalTextureCreate(RenderingDevice.ExternalTextureShareHandleType.Iosurface,
            RenderingDevice.DataFormat.R8G8B8A8Unorm, (uint)width, (uint)height);
        if (!rid.IsValid)
            throw new NotSupportedException("external texture creation failed (Iosurface)");

        var sharedEvent = Metal.NewSharedEvent(_device);
        if (sharedEvent == 0)
        {
            rd.FreeRid(rid);
            throw new PlatformNotSupportedException("MTLSharedEvent creation failed");
        }
        ICompositionImportedGpuSemaphore semaphore;
        try
        {
            semaphore = interop.ImportSemaphore(new PlatformHandle(sharedEvent,
                KnownPlatformGraphicsExternalSemaphoreHandleTypes.MetalSharedEvent));
        }
        catch
        {
            Metal.Release(sharedEvent);
            rd.FreeRid(rid);
            throw;
        }
        return new IOSurfaceSharedTexture(rd, rid, width, height, sharedEvent, semaphore);
    }

    public void Dispose()
    {
        if (_device != 0) Metal.Release(_device);
        _device = 0;
    }

    private sealed class IOSurfaceSharedTexture(
        RenderingDevice rd, Rid rid, int width, int height, nint sharedEvent,
        ICompositionImportedGpuSemaphore semaphore) : ISharedTexture
    {
        // Timeline per present: the host signals 'wait' (= copy done), the compositor signals
        // 'wait + 1' after reading. The buffer is writable again once the event reached the
        // last release value.
        private ulong _value;
        private ulong _releaseValue;

        public int Width => width;
        public int Height => height;
        public Rid Rid => rid;

        public IPlatformHandle Handle { get; } =
            new PlatformHandle((nint)rd.ExternalTextureGetHandle(rid),
                KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef);

        public PlatformGraphicsExternalImageProperties ImportProperties => new()
        {
            Width = width,
            Height = height,
            Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
            TopLeftOrigin = true,
        };

        public AcquireResult Acquire()
        {
            if (semaphore.IsLost) return AcquireResult.Failed;
            // The compositor still reads this buffer: its release signal has not landed yet.
            return Metal.SignaledValue(sharedEvent) < _releaseValue ? AcquireResult.Busy : AcquireResult.Acquired;
        }

        public bool Release() => true;

        public Task PresentAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image)
        {
            // The engine's present stalled until the copy completed on the GPU, so the wait
            // value can be signaled from the CPU right away; the compositor signals the
            // release value once its sampling command buffer retired.
            var wait = _value + 1;
            var release = _value + 2;
            _value = release;
            _releaseValue = release;
            Metal.SetSignaledValue(sharedEvent, wait);
            return surface.UpdateWithTimelineSemaphoresAsync(image, semaphore, wait, semaphore, release);
        }

        public void HandleImported()
        {
        }

        public void Dispose()
        {
            rd.FreeRid(rid);
            var dispose = semaphore.DisposeAsync();
            if (!dispose.IsCompletedSuccessfully)
                _ = dispose.AsTask().ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            // The compositor retained the event on import; this drops the creation reference.
            Metal.Release(sharedEvent);
        }
    }

    /// <summary>The few Metal/ObjC entry points the shared-event round trip needs.</summary>
    private static class Metal
    {
        private const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        private static readonly nint SelNewSharedEvent = sel_registerName("newSharedEvent");
        private static readonly nint SelSignaledValue = sel_registerName("signaledValue");
        private static readonly nint SelSetSignaledValue = sel_registerName("setSignaledValue:");
        private static readonly nint SelRelease = sel_registerName("release");

        [DllImport(MetalFramework)]
        public static extern nint MTLCreateSystemDefaultDevice();

        // 'new...' returns an owned (+1) reference.
        public static nint NewSharedEvent(nint device) => objc_msgSend(device, SelNewSharedEvent);

        public static ulong SignaledValue(nint sharedEvent) => objc_msgSend_ulong(sharedEvent, SelSignaledValue);

        public static void SetSignaledValue(nint sharedEvent, ulong value) =>
            objc_msgSend_setulong(sharedEvent, SelSetSignaledValue, value);

        public static void Release(nint obj) => objc_msgSend(obj, SelRelease);

        [DllImport(ObjC)]
        private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(ObjC)]
        private static extern nint objc_msgSend(nint receiver, nint selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern ulong objc_msgSend_ulong(nint receiver, nint selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_setulong(nint receiver, nint selector, ulong value);
    }
}
