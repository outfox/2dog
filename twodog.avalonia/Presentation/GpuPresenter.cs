using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Godot;
using Vector2 = System.Numerics.Vector2;

namespace twodog.Presentation;

/// <summary>
/// Zero-copy presenter: the engine copies its main viewport into a shared GPU texture each
/// frame, and Avalonia's compositor imports that texture directly - no CPU round trip.
/// Windows-only for now (D3D11 keyed-mutex sharing; the engine's Vulkan device imports the
/// host-created texture). Initialization is asynchronous; on failure the session falls back
/// to the CPU presenter when the mode allows it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class GpuPresenter : IPresenter
{
    internal enum InitState { Initializing, Ready, Failed }

    private readonly GodotControl _control;
    private readonly GodotSession _session;
    private D3D11SharedTextureProvider? _provider;
    private ICompositionGpuInterop? _interop;
    private CompositionDrawingSurface? _surface;
    private CompositionSurfaceVisual? _visual;
    private ICompositionImportedGpuImage? _imported;
    private RenderingDevice? _rd;
    private Rid _viewportTexture;
    private Rid _sharedRid;
    private Task? _lastPresent;

    public InitState State { get; private set; } = InitState.Initializing;

    public bool Failed => State == InitState.Failed;

    /// <summary>Whether the running engine's natives can share textures. Engine must be started.</summary>
    public static bool IsSupported()
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return false;
        var kmtBit = 1u << (int)RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex;
        return (rd.ExternalTextureGetSupportedHandleTypes() & kmtBit) != 0;
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
            var interop = await compositor.TryGetCompositionGpuInterop()
                          ?? throw new PlatformNotSupportedException("The compositor offers no GPU interop.");
            if (!interop.SupportedImageHandleTypes.Contains(
                    KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
                throw new PlatformNotSupportedException("The compositor cannot import D3D11 shared handles.");

            _interop = interop;
            _provider = new D3D11SharedTextureProvider(interop.DeviceLuid);
            _surface = compositor.CreateDrawingSurface();
            _visual = compositor.CreateSurfaceVisual();
            _visual.Surface = _surface;
            _visual.Size = new Vector2((float)_control.Bounds.Width, (float)_control.Bounds.Height);
            ElementComposition.SetElementChildVisual(_control, _visual);
            State = InitState.Ready;
        }
        catch (Exception)
        {
            State = InitState.Failed;
            ReleaseResources();
        }
    }

    public void PresentFrame()
    {
        if (State != InitState.Ready || !_session.IsStarted || _provider is null || _surface is null) return;
        // One image in flight: skip the frame while the compositor still owns the last update,
        // and skip when the keyed mutex cannot be acquired promptly (compositor mid-read).
        if (_lastPresent is { IsCompleted: false }) return;

        _rd ??= RenderingServer.GetRenderingDevice();
        if (_rd is null) return;

        var size = DisplayServer.WindowGetSize();
        if (size.X <= 0 || size.Y <= 0) return;

        if ((_provider.Width != size.X || _provider.Height != size.Y || !_sharedRid.IsValid)
            && !RecreateSharedTexture(_rd, size.X, size.Y))
        {
            State = InitState.Failed;
            ReleaseResources();
            return;
        }

        if (!_provider.Acquire(timeoutMs: 100)) return;
        Error err;
        try
        {
            // The root viewport's texture RID is engine-lifetime stable; only the RD-level
            // texture behind it changes (on resize), so that one is re-queried per frame.
            if (!_viewportTexture.IsValid)
                _viewportTexture = RenderingServer.ViewportGetTexture(_session.Engine.Tree.Root.GetViewportRid());
            var viewportRd = RenderingServer.TextureGetRdTexture(_viewportTexture);
            err = _rd.ExternalTexturePresent(viewportRd, _sharedRid);
        }
        finally
        {
            _provider.Release();
        }

        if (err != Error.Ok || _imported is null) return;
        _lastPresent = _surface.UpdateWithKeyedMutexAsync(_imported, 1, 0);
    }

    private bool RecreateSharedTexture(RenderingDevice rd, int width, int height)
    {
        _imported = null; // Superseded imports are released with the presenter.
        if (_sharedRid.IsValid)
        {
            rd.FreeRid(_sharedRid);
            _sharedRid = default;
        }

        try
        {
            _provider!.CreateTexture(width, height);
        }
        catch (Exception)
        {
            return false;
        }

        _sharedRid = rd.ExternalTextureCreate(
            RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex,
            RenderingDevice.DataFormat.R8G8B8A8Unorm,
            (uint)width, (uint)height, (ulong)_provider.SharedHandle);
        if (!_sharedRid.IsValid) return false;

        _imported = _interop!.ImportImage(
            new PlatformHandle(_provider.SharedHandle,
                KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
            new PlatformGraphicsExternalImageProperties
            {
                Width = width,
                Height = height,
                Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
                // The engine writes rows top-down; without this the GL-backed compositor
                // assumes a bottom-left origin and shows the viewport flipped.
                TopLeftOrigin = true,
            });
        return true;
    }

    public void Resize()
    {
        // The session already resized the engine window; the shared texture follows on the
        // next PresentFrame. Only the composition visual needs the new control size.
        if (_visual is not null)
            _visual.Size = new Vector2((float)_control.Bounds.Width, (float)_control.Bounds.Height);
    }

    private void ReleaseResources()
    {
        ElementComposition.SetElementChildVisual(_control, null);
        _imported = null;
        if (_sharedRid.IsValid && _session.IsStarted && (_rd ?? RenderingServer.GetRenderingDevice()) is { } rd)
            rd.FreeRid(_sharedRid);
        _sharedRid = default;
        _viewportTexture = default;
        _rd = null;
        _provider?.Dispose();
        _provider = null;
        _surface = null;
        _visual = null;
    }

    public void Dispose() => ReleaseResources();
}
