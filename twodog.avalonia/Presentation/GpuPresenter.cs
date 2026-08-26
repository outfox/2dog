using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Godot;
using Dispatcher = Avalonia.Threading.Dispatcher;
using Vector2 = System.Numerics.Vector2;

namespace twodog.Presentation;

/// <summary>
/// Zero-copy presenter: the engine copies its main viewport into a shared GPU texture each
/// frame, and Avalonia's compositor imports that texture directly - no CPU round trip.
/// Sharing flavors per platform: Windows imports a host-created D3D11 keyed-mutex texture
/// into the engine's Vulkan device; Linux and macOS import the engine's exported memory
/// (Vulkan opaque fd, IOSurface) into the compositor. Initialization is asynchronous; on
/// failure the session falls back to the CPU presenter when the mode allows it.
/// </summary>
internal sealed class GpuPresenter : IPresenter
{
    private enum InitState { Initializing, Ready, Failed }

    // One engine copy target plus its compositor-side import; the presenter rotates through
    // the factory's BufferCount of these so unsynchronized flavors never write the image the
    // compositor still samples.
    private readonly record struct SharedBuffer(ISharedTexture Texture, ICompositionImportedGpuImage Imported);

    private readonly GodotControl _control;
    private readonly GodotSession _session;
    private ISharedTextureFactory? _factory;
    private SharedBuffer[] _buffers = [];
    private int _nextBuffer;
    private ICompositionGpuInterop? _interop;
    private CompositionDrawingSurface? _surface;
    private CompositionSurfaceVisual? _visual;
    private RenderingDevice? _rd;
    private Task? _lastPresent;
    private InitState _state = InitState.Initializing;
    private bool _disposed;
    private int _presentCount;
    private int _pendingSkips;

    // Zero-copy support varies per compositor and driver; the exact failure (unsupported
    // import, faulted present) is otherwise swallowed by the CPU fallback. Opt-in via
    // TWODOG_AVALONIA_DIAG=1 when triaging a platform.
    private static readonly bool Diag = System.Environment.GetEnvironmentVariable("TWODOG_AVALONIA_DIAG") == "1";
    private static void Log(string message)
    {
        if (Diag) Console.WriteLine($"2DOG_GPU: {message}");
    }

    public bool Ready => _state == InitState.Ready;

    public bool Failed => _state == InitState.Failed;

    /// <summary>This platform's sharing flavors: the engine share types it can ride on plus
    /// the factory creating one - the single table both <see cref="IsSupported"/> and
    /// initialization consume. The factory receives the engine's supported-types mask and
    /// picks the concrete flavor. Null when the platform has no zero-copy flavor yet.</summary>
    private static (RenderingDevice.ExternalTextureShareHandleType[] ShareTypes,
        Func<ICompositionGpuInterop, uint, ISharedTextureFactory> CreateFactory)? PlatformSharing
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return ([RenderingDevice.ExternalTextureShareHandleType.D3D11NtKeyedMutex,
                        RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex],
                    CreateD3D11Factory);
            if (OperatingSystem.IsLinux())
                return Exported(RenderingDevice.ExternalTextureShareHandleType.OpaqueFd,
                    KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor,
                    needsMemorySize: true);
            if (OperatingSystem.IsMacOS())
                return ([RenderingDevice.ExternalTextureShareHandleType.Iosurface],
                    (interop, _) => new IOSurfaceSharedTextureFactory(interop));
            return null;

            static (RenderingDevice.ExternalTextureShareHandleType[],
                Func<ICompositionGpuInterop, uint, ISharedTextureFactory>) Exported(
                    RenderingDevice.ExternalTextureShareHandleType shareType, string handleType,
                    bool needsMemorySize) =>
                ([shareType], (_, _) => new EngineExportedTextureFactory(shareType, handleType, needsMemorySize));
        }
    }

    [SupportedOSPlatform("windows")]
    private static ISharedTextureFactory CreateD3D11Factory(ICompositionGpuInterop interop, uint engineTypes)
    {
        // NT handles are preferred: some drivers (2020-era Intel) import only NT, never KMT.
        var nt = (engineTypes & (1u << (int)RenderingDevice.ExternalTextureShareHandleType.D3D11NtKeyedMutex)) != 0
                 && interop.SupportedImageHandleTypes.Contains(
                     KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle);
        return new D3D11SharedTextureFactory(interop.DeviceLuid, nt);
    }

    /// <summary>Whether the running engine's natives can share textures on this platform.
    /// The engine must be started.</summary>
    public static bool IsSupported()
    {
        if (PlatformSharing is not { } sharing) return false;
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return false;
        var engineTypes = rd.ExternalTextureGetSupportedHandleTypes();
        return sharing.ShareTypes.Any(t => (engineTypes & (1u << (int)t)) != 0);
    }

    public GpuPresenter(GodotControl control, GodotSession session)
    {
        _control = control;
        _session = session;
        _ = InitializeAsync();
    }

    public GodotPresentationMode Mode => GodotPresentationMode.Gpu;

    private async Task InitializeAsync()
    {
        try
        {
            var selfVisual = ElementComposition.GetElementVisual(_control)
                             ?? throw new InvalidOperationException("The control is not composited.");
            var compositor = selfVisual.Compositor;
            // EGL backends miss the compositor-side import feature; see the shim.
            await EglExternalObjectsShim.TryRepairAsync(compositor);
            var interop = await compositor.TryGetCompositionGpuInterop()
                          ?? throw new PlatformNotSupportedException("The compositor offers no GPU interop.");
            // Disposed while awaiting (detach, presenter swap): the control may already belong
            // to a successor - allocate nothing and leave its child visual alone.
            if (_disposed) return;

            Log($"interop supported handle types: {string.Join(",", interop.SupportedImageHandleTypes)}");
            var sharing = PlatformSharing
                ?? throw new PlatformNotSupportedException("No zero-copy sharing flavor for this platform.");
            // Forced-Gpu mode may initialize before Start(); the engine mask is then unknown
            // and the factory's optimistic pick is re-checked by texture creation itself.
            var engineTypes = _session.IsStarted && RenderingServer.GetRenderingDevice() is { } engineRd
                ? engineRd.ExternalTextureGetSupportedHandleTypes()
                : ~0u;
            var factory = sharing.CreateFactory(interop, engineTypes);
            if (!interop.SupportedImageHandleTypes.Contains(factory.AvaloniaHandleType))
            {
                factory.Dispose();
                throw new PlatformNotSupportedException(
                    $"The compositor cannot import {factory.AvaloniaHandleType}.");
            }
            // Importable is not presentable: the first frame would fault (or never show)
            // when the compositor lacks the synchronization flavor PresentAsync engages.
            var sync = interop.GetSynchronizationCapabilities(factory.AvaloniaHandleType);
            if ((sync & factory.RequiredSynchronization) == 0)
            {
                factory.Dispose();
                throw new PlatformNotSupportedException(
                    $"The compositor lacks {factory.RequiredSynchronization} synchronization " +
                    $"for {factory.AvaloniaHandleType} (supports: {sync}).");
            }

            Log($"interop ok; supported handle types: {string.Join(",", interop.SupportedImageHandleTypes)}; sync: {sync}");
            _factory = factory;
            _interop = interop;
            _surface = compositor.CreateDrawingSurface();
            _visual = compositor.CreateSurfaceVisual();
            _visual.Surface = _surface;
            _visual.Size = new Vector2((float)_control.Bounds.Width, (float)_control.Bounds.Height);
            ElementComposition.SetElementChildVisual(_control, _visual);
            _state = InitState.Ready;
        }
        catch (Exception ex)
        {
            Log($"init failed: {ex}");
            _state = InitState.Failed;
            if (!_disposed) ReleaseResources();
        }
    }

    public void PresentFrame()
    {
        if (_state != InitState.Ready || !_session.IsStarted || _factory is null || _surface is null) return;
        // One image in flight: skip the frame while the compositor still owns the last update,
        // and skip when the writer-side sync cannot engage promptly (compositor mid-read).
        if (_lastPresent is { } present)
        {
            if (!present.IsCompleted)
            {
                if (++_pendingSkips % 300 == 0) Log($"still waiting on present #{_presentCount} ({_pendingSkips} skips)");
                return;
            }
            _pendingSkips = 0;
            if (!present.IsCompletedSuccessfully)
            {
                // The compositor rejected or lost the imported image (failed import, device
                // loss); observe the fault and fail over instead of retrying every frame.
                var fault = present.Exception?.GetBaseException();
                Log($"present task faulted: {fault}");
                Fail();
                return;
            }
        }

        // The session is started, so a missing RenderingDevice is terminal (a renderer
        // without one, e.g. GL Compatibility, can never share textures); failing here keeps
        // a forced-GPU session from reporting Ready while presenting nothing.
        _rd ??= RenderingServer.GetRenderingDevice();
        if (_rd is null)
        {
            Log("no rendering device; this renderer cannot share textures");
            Fail();
            return;
        }

        var size = DisplayServer.WindowGetSize();
        if (size.X <= 0 || size.Y <= 0) return;

        if ((_buffers.Length == 0 || _buffers[0].Texture.Width != size.X || _buffers[0].Texture.Height != size.Y)
            && !RecreateSharedBuffers(_rd, size.X, size.Y))
        {
            Fail();
            return;
        }

        var (texture, imported) = _buffers[_nextBuffer];
        switch (texture.Acquire())
        {
            case AcquireResult.Busy: return;
            case AcquireResult.Failed: Fail(); return;
        }
        Error err;
        bool released;
        try
        {
            // Only the RD-level texture behind the (stable, session-cached) viewport texture
            // changes on resize, so it is re-queried per frame.
            var viewportRd = RenderingServer.TextureGetRdTexture(_session.ViewportTexture);
            err = _rd.ExternalTexturePresent(viewportRd, texture.Rid);
        }
        finally
        {
            released = texture.Release();
        }
        if (!released)
        {
            Log("release failed");
            Fail();
            return;
        }

        if (err != Error.Ok)
        {
            Log($"present failed: err={err}");
            Fail();
            return;
        }
        try
        {
            _lastPresent = texture.PresentAsync(_surface, imported);
        }
        catch (Exception ex)
        {
            // A synchronous rejection (disposed surface, lost image) must fail over to the
            // CPU path like an async fault, not escape into the UI thread's frame callback.
            Log($"present threw: {ex}");
            Fail();
            return;
        }
        _nextBuffer = (_nextBuffer + 1) % _buffers.Length;
        _presentCount++;
        if (_presentCount <= 3 || _presentCount % 300 == 0) Log($"present #{_presentCount} queued");
    }

    private bool RecreateSharedBuffers(RenderingDevice rd, int width, int height)
    {
        // The in-flight gate has already passed, so no pending update reads the old buffers;
        // their compositor-side release is still asynchronous and merely observed.
        DisposeBuffers(_buffers, engineAlive: true);
        _buffers = [];
        _nextBuffer = 0;

        var buffers = new SharedBuffer[_factory!.BufferCount];
        try
        {
            for (var i = 0; i < buffers.Length; i++)
            {
                var texture = _factory.Create(rd, width, height);
                ICompositionImportedGpuImage imported;
                try
                {
                    imported = _interop!.ImportImage(texture.Handle, texture.ImportProperties);
                }
                catch
                {
                    texture.Dispose();
                    throw;
                }
                texture.HandleImported();
                buffers[i] = new SharedBuffer(texture, imported);
            }
            _buffers = buffers;
            Log($"created+imported {buffers.Length}x {width}x{height} memorySize={buffers[0].Texture.ImportProperties.MemorySize}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"create/import {width}x{height} failed: {ex}");
            DisposeBuffers(buffers, engineAlive: true);
            return false;
        }
    }

    // Imported-image releases are asynchronous; their faults are logged instead of left
    // unobserved (an unobserved fault would surface as UnobservedTaskException noise).
    private static void Observe(ValueTask dispose)
    {
        if (dispose.IsCompletedSuccessfully) return;
        _ = dispose.AsTask().ContinueWith(
            t => Log($"imported image release failed: {t.Exception?.GetBaseException()}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void DisposeBuffers(SharedBuffer[] buffers, bool engineAlive)
    {
        foreach (var (texture, imported) in buffers)
        {
            if (imported is not null) Observe(imported.DisposeAsync());
            // Engine-side RIDs die with the engine; freeing them afterwards would call into
            // a torn-down RenderingDevice.
            if (engineAlive) texture?.Dispose();
        }
    }

    public void Resize()
    {
        // The session already resized the engine window; the shared texture follows on the
        // next PresentFrame. Only the composition visual needs the new control size.
        if (_visual is not null)
            _visual.Size = new Vector2((float)_control.Bounds.Width, (float)_control.Bounds.Height);
    }

    /// <summary>Terminal presenter failure: the session's reconcile pass observes
    /// <see cref="Failed"/> on the next pump and swaps in the CPU fallback under Auto.</summary>
    private void Fail()
    {
        _state = InitState.Failed;
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        ElementComposition.SetElementChildVisual(_control, null);
        var buffers = _buffers;
        var factory = _factory;
        _buffers = [];
        _nextBuffer = 0;
        _factory = null;
        _interop = null;
        _rd = null;
        _surface = null;
        _visual = null;
        var present = _lastPresent;
        _lastPresent = null;

        if (PendingCompositorWork(buffers, present) is not { } pending)
        {
            DisposeBuffers(buffers, engineAlive: _session.IsStarted);
            factory?.Dispose();
            return;
        }
        // An update or import is still in flight: destroying the imported images and their
        // backing textures now could pull the GPU memory out from under the compositor.
        // Defer until it settles, back on the UI thread (RenderingDevice affinity). The
        // session may be disposed by then; DisposeBuffers skips dead engine-side RIDs.
        var session = _session;
        pending.ContinueWith(t =>
        {
            _ = t.Exception;
            Dispatcher.UIThread.Post(() =>
            {
                DisposeBuffers(buffers, engineAlive: session.IsStarted);
                factory?.Dispose();
            });
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private static Task? PendingCompositorWork(SharedBuffer[] buffers, Task? present)
    {
        List<Task>? pending = null;
        Add(present);
        foreach (var buffer in buffers) Add(buffer.Imported?.ImportCompleted);
        return pending is null ? null : Task.WhenAll(pending);

        void Add(Task? task)
        {
            if (task is { IsCompleted: false }) (pending ??= []).Add(task);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseResources();
    }
}
