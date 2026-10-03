using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Godot;
using static twodog.Presentation.D3D11Interop;

namespace twodog.Presentation;

/// <summary>
/// Import-style sharing for Windows: this side creates a D3D11 keyed-mutex shared texture on
/// the compositor's adapter, the engine imports its shared handle into Vulkan, and Avalonia
/// imports the same handle. NT handles are preferred (some drivers, e.g. 2020-era Intel,
/// import only those); legacy KMT global shared handles remain the fallback. The keyed mutex
/// is driven from the CPU around the engine's copy - writer acquires key 0 and releases
/// key 1; the compositor presents with (1, 0).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D11SharedTextureFactory : ISharedTextureFactory
{
    private const uint SharedResourceRead = 0x80000000;
    private const uint SharedResourceWrite = 0x00000001;

    private readonly bool _ntHandle;
    private void* _device;

    public string AvaloniaHandleType => _ntHandle
        ? KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle
        : KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle;

    public CompositionGpuImportedImageSynchronizationCapabilities RequiredSynchronization =>
        CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex;

    // The keyed mutex serializes the engine's writes against compositor reads on one texture.
    public int BufferCount => 1;

    /// <summary>Creates the device on the adapter with the given LUID (the compositor's device).</summary>
    public D3D11SharedTextureFactory(byte[]? adapterLuid, bool ntHandle)
    {
        _ntHandle = ntHandle;
        var adapter = FindAdapter(adapterLuid);
        try
        {
            void* device = null;
            // A specific adapter requires DriverType Unknown per D3D11CreateDevice rules. No immediate
            // context: this device only creates resources, and the keyed mutex synchronizes access.
            ThrowIfFailed(D3D11CreateDevice(
                adapter, adapter is null ? DriverTypeHardware : DriverTypeUnknown,
                0, CreateDeviceBgraSupport, null, 0, SdkVersion, &device, null, null));
            _device = device;
        }
        finally
        {
            if (adapter is not null) D3D11Interop.Release(adapter);
        }
    }

    /// <summary>The adapter with this LUID (caller releases), or null for the default adapter.</summary>
    private static void* FindAdapter(byte[]? luid)
    {
        if (luid is not { Length: 8 }) return null;
        var target = BitConverter.ToInt64(luid, 0);

        var factory = TryCreateFactory1();
        if (factory is null)
            throw new InvalidOperationException("DXGI could not create a factory.");
        try
        {
            for (uint i = 0; ; i++)
            {
                void* adapter = null;
                if (EnumAdapters1(factory, i, &adapter) != 0) break;
                AdapterDesc1 desc;
                GetDesc1(adapter, &desc);
                if (desc.Luid == target) return adapter;
                D3D11Interop.Release(adapter);
            }
        }
        finally
        {
            D3D11Interop.Release(factory);
        }
        return null;
    }

    public ISharedTexture Create(RenderingDevice rd, int width, int height)
    {
        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = FormatR8G8B8A8Unorm,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = UsageDefault,
            BindFlags = BindRenderTarget | BindShaderResource,
            MiscFlags = _ntHandle ? MiscSharedKeyedMutex | MiscSharedNtHandle : MiscSharedKeyedMutex,
        };
        void* texture = null;
        ThrowIfFailed(CreateTexture2D(_device, &desc, null, &texture));

        // Staged acquisition: any failure past this point must release what already exists,
        // or the CPU-fallback path leaks the texture and mutex on every attempt.
        void* mutex = null;
        try
        {
            var mutexIid = IidKeyedMutex;
            ThrowIfFailed(QueryInterface(texture, &mutexIid, &mutex));

            var shared = _ntHandle ? CreateNtHandle(texture) : GetKmtHandle(texture);
            try
            {
                var rid = rd.ExternalTextureCreate(
                    _ntHandle
                        ? RenderingDevice.ExternalTextureShareHandleType.D3D11NtKeyedMutex
                        : RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex,
                    RenderingDevice.DataFormat.R8G8B8A8Unorm,
                    (uint)width, (uint)height, (ulong)shared);
                if (!rid.IsValid)
                    throw new NotSupportedException("The engine could not import the D3D11 shared texture.");

                return new D3D11SharedTexture(rd, rid, texture, mutex, shared, width, height,
                    AvaloniaHandleType, ownsHandle: _ntHandle);
            }
            catch
            {
                // Neither the engine's Vulkan import nor Avalonia's takes NT handle
                // ownership; without this close each failed attempt leaks the handle.
                if (_ntHandle) CloseHandle(shared);
                throw;
            }
        }
        catch
        {
            if (mutex is not null) D3D11Interop.Release(mutex);
            D3D11Interop.Release(texture);
            throw;
        }
    }

    /// <summary>The legacy KMT global shared handle - not an owned resource.</summary>
    private static nint GetKmtHandle(void* texture)
    {
        void* resource = null;
        var resourceIid = IidResource;
        ThrowIfFailed(QueryInterface(texture, &resourceIid, &resource));
        try
        {
            nint handle = 0;
            ThrowIfFailed(GetSharedHandle(resource, &handle));
            return handle;
        }
        finally
        {
            D3D11Interop.Release(resource);
        }
    }

    /// <summary>An owned NT handle the caller must eventually close.</summary>
    private static nint CreateNtHandle(void* texture)
    {
        void* resource = null;
        var resourceIid = IidResource1;
        ThrowIfFailed(QueryInterface(texture, &resourceIid, &resource));
        try
        {
            nint handle = 0;
            ThrowIfFailed(CreateSharedHandle(resource, null, SharedResourceRead | SharedResourceWrite, null, &handle));
            return handle;
        }
        finally
        {
            D3D11Interop.Release(resource);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    public void Dispose()
    {
        if (_device is null) return;
        D3D11Interop.Release(_device);
        _device = null;
    }

    private sealed class D3D11SharedTexture(
        RenderingDevice rd, Rid rid, void* texture, void* mutex,
        nint sharedHandle, int width, int height, string handleType, bool ownsHandle) : ISharedTexture
    {
        private void* _texture = texture;
        private void* _mutex = mutex;

        public int Width => width;
        public int Height => height;
        public Rid Rid => rid;

        public IPlatformHandle Handle { get; } = new PlatformHandle(sharedHandle, handleType);

        public PlatformGraphicsExternalImageProperties ImportProperties => new()
        {
            Width = width,
            Height = height,
            Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
            // The engine writes rows top-down; without this the GL-backed compositor assumes
            // a bottom-left origin and shows the viewport flipped.
            TopLeftOrigin = true,
        };

        public AcquireResult Acquire()
        {
            if (_mutex is null) return AcquireResult.Failed;
            return AcquireSync(_mutex, 0, 0) switch
            {
                0 => AcquireResult.Acquired,
                // AcquireSync reports contention as WAIT_TIMEOUT (0x102, success severity; some
                // layers wrap it as 0x80070102). Anything else - device removed, abandoned
                // mutex - is terminal.
                0x102 or unchecked((int)0x80070102) => AcquireResult.Busy,
                _ => AcquireResult.Failed,
            };
        }

        public bool Release() => _mutex is not null && ReleaseSync(_mutex, 1) == 0;

        // The complement of the writer's protocol: the writer acquired 0 and released 1, so
        // the compositor presents with (1, 0).
        public Task PresentAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image) =>
            surface.UpdateWithKeyedMutexAsync(image, 1, 0);

        // Nothing transfers on import: a KMT global shared handle is not an owned resource,
        // and neither the engine's Vulkan import nor Avalonia's OpenSharedResource1 consumes
        // an NT handle - it stays this side's to close.
        public void HandleImported()
        {
        }

        public void Dispose()
        {
            rd.FreeRid(rid);
            if (_mutex is not null) D3D11Interop.Release(_mutex);
            _mutex = null;
            if (_texture is not null) D3D11Interop.Release(_texture);
            _texture = null;
            if (ownsHandle) CloseHandle(sharedHandle);
        }
    }
}
