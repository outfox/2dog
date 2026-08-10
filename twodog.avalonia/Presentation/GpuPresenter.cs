using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Godot;
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

    private readonly GodotControl _control;
    private readonly GodotSession _session;
    private ISharedTextureFactory? _factory;
    private ISharedTexture? _texture;
    private ICompositionGpuInterop? _interop;
    private CompositionDrawingSurface? _surface;
    private CompositionSurfaceVisual? _visual;
    private ICompositionImportedGpuImage? _imported;
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

    /// <summary>This platform's sharing flavor: the engine share type it rides on plus the
    /// factory creating it - the single table both <see cref="IsSupported"/> and
    /// initialization consume. Null when the platform has no zero-copy flavor yet.</summary>
    private static (RenderingDevice.ExternalTextureShareHandleType ShareType,
        Func<ICompositionGpuInterop, ISharedTextureFactory> CreateFactory)? PlatformSharing
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return (RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex, CreateD3D11Factory);
            if (OperatingSystem.IsLinux())
                return Exported(RenderingDevice.ExternalTextureShareHandleType.OpaqueFd,
                    KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor,
                    needsMemorySize: true);
            if (OperatingSystem.IsMacOS())
                return Exported(RenderingDevice.ExternalTextureShareHandleType.Iosurface,
                    KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef,
                    needsMemorySize: false);
            return null;

            static (RenderingDevice.ExternalTextureShareHandleType,
                Func<ICompositionGpuInterop, ISharedTextureFactory>) Exported(
                    RenderingDevice.ExternalTextureShareHandleType shareType, string handleType,
                    bool needsMemorySize) =>
                (shareType, _ => new EngineExportedTextureFactory(shareType, handleType, needsMemorySize));
        }
    }

    [SupportedOSPlatform("windows")]
    private static ISharedTextureFactory CreateD3D11Factory(ICompositionGpuInterop interop) =>
        new D3D11SharedTextureFactory(interop.DeviceLuid);

    /// <summary>Whether the running engine's natives can share textures on this platform.
    /// The engine must be started.</summary>
    public static bool IsSupported()
    {
        if (PlatformSharing is not { } sharing) return false;
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return false;
        return (rd.ExternalTextureGetSupportedHandleTypes() & (1u << (int)sharing.ShareType)) != 0;
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
            var factory = sharing.CreateFactory(interop);
            if (!interop.SupportedImageHandleTypes.Contains(factory.AvaloniaHandleType))
            {
                factory.Dispose();
                throw new PlatformNotSupportedException(
                    $"The compositor cannot import {factory.AvaloniaHandleType}.");
            }

            Log($"interop ok; supported handle types: {string.Join(",", interop.SupportedImageHandleTypes)}");
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

        if ((_texture is null || _texture.Width != size.X || _texture.Height != size.Y)
            && !RecreateSharedTexture(_rd, size.X, size.Y))
        {
            Fail();
            return;
        }

        switch (_texture!.Acquire())
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
            err = _rd.ExternalTexturePresent(viewportRd, _texture.Rid);
        }
        finally
        {
            released = _texture.Release();
        }
        if (!released)
        {
            Log("release failed");
            Fail();
            return;
        }

        if (err != Error.Ok || _imported is null)
        {
            Log($"present failed: err={err} imported={_imported is not null}");
            Fail();
            return;
        }
        _lastPresent = _texture.PresentAsync(_surface, _imported);
        _presentCount++;
        if (_presentCount <= 3 || _presentCount % 300 == 0) Log($"present #{_presentCount} queued");
    }

    private bool RecreateSharedTexture(RenderingDevice rd, int width, int height)
    {
        _ = _imported?.DisposeAsync();
        _imported = null;
        _texture?.Dispose();
        _texture = null;

        try
        {
            _texture = _factory!.Create(rd, width, height);
            _imported = _interop!.ImportImage(_texture.Handle, _texture.ImportProperties);
            Log($"created+imported {width}x{height} handle={_texture.Handle.Handle} memorySize={_texture.ImportProperties.MemorySize}");
            return _imported is not null;
        }
        catch (Exception ex)
        {
            Log($"create/import {width}x{height} failed: {ex}");
            _texture?.Dispose();
            _texture = null;
            return false;
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
        _ = _imported?.DisposeAsync();
        _imported = null;
        _texture?.Dispose();
        _texture = null;
        _factory?.Dispose();
        _factory = null;
        _interop = null;
        _rd = null;
        _surface = null;
        _visual = null;
        _lastPresent = null;
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseResources();
    }
}
