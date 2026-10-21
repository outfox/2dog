using System;
using Godot;
using ShareType = Godot.RenderingDevice.ExternalTextureShareHandleType;

namespace twodog.Presentation;

/// <summary>
/// Zero-copy presenter for Qt Quick's Direct3D 11 backend: the view creates D3D11 shared textures on Qt's adapter,
/// the engine imports them into its Vulkan device, and each frame the engine copies its viewport into one of them.
/// The view rotates three buffers and hands them between engine and Qt (see native/godotview.cpp).
/// </summary>
internal sealed unsafe class GpuPresenter : IPresenter
{
    // QSGRendererInterface::Direct3D11.
    private const int Direct3D11 = 4;

    private readonly nint _view;
    private readonly RenderingDevice _rd;
    private readonly bool _allowNtHandles;
    private Rid[] _textures = [];
    private Vector2I _size;
    private bool _transparent;
    private bool _failed;

    private GpuPresenter(nint view, RenderingDevice rd, bool allowNtHandles)
    {
        _view = view;
        _rd = rd;
        _allowNtHandles = allowNtHandles;
    }

    /// <summary>A presenter when the view's backend and the engine's natives can share textures, else null.</summary>
    public static IPresenter? TryCreate(nint view)
    {
        // shortcut: Direct3D 11 only, Qt Quick's default on Windows; other backends (Metal, OpenGL, Vulkan) use the
        // CPU fallback until their imports land.
        if (!OperatingSystem.IsWindows() || Native.ViewGraphicsApi(view, out _) != Direct3D11) return null;
        if (RenderingServer.GetRenderingDevice() is not { } rd) return null;
        var types = rd.ExternalTextureGetSupportedHandleTypes();
        var nt = Supports(types, ShareType.D3D11NtKeyedMutex);
        return nt || Supports(types, ShareType.D3D11KmtKeyedMutex) ? new GpuPresenter(view, rd, nt) : null;
    }

    private static bool Supports(uint types, ShareType type) => (types & (1u << (int)type)) != 0;

    public QtPresentationMode Mode => QtPresentationMode.Gpu;

    public bool Failed => _failed;

    public void Present(Rid viewportTexture)
    {
        if (_failed) return;
        var size = DisplayServer.WindowGetSize();
        if (size.X <= 0 || size.Y <= 0) return;
        // Only a transparent viewport writes meaningful alpha; otherwise Qt must draw the frame opaque.
        var transparent = ((SceneTree)Godot.Engine.GetMainLoop()).Root.TransparentBg;
        if ((size != _size || transparent != _transparent) && !Recreate(size, transparent))
        {
            Fail();
            return;
        }
        var index = Native.GpuBeginWrite(_view);
        // Qt still samples or has not yet returned every buffer: this engine frame is not shown.
        if (index == -1) return;
        if (index < 0)
        {
            Fail();
            return;
        }
        // The copy stalls until it has finished on the GPU, so Qt may sample the buffer as soon as it is released.
        var error = _rd.ExternalTexturePresent(RenderingServer.TextureGetRdTexture(viewportTexture), _textures[index]);
        Native.GpuEndWrite(_view, index, error == Error.Ok);
        if (error != Error.Ok || Native.GpuFailed(_view)) Fail();
    }

    private bool Recreate(Vector2I size, bool transparent)
    {
        FreeTextures();
        var handles = stackalloc ulong[3];
        var count = Native.GpuCreate(_view, size.X, size.Y, _allowNtHandles, !transparent, handles, out var ntHandles);
        if (count == 0) return false;
        _textures = new Rid[count];
        for (var i = 0; i < count; i++)
        {
            _textures[i] = _rd.ExternalTextureCreate(ntHandles ? ShareType.D3D11NtKeyedMutex : ShareType.D3D11KmtKeyedMutex,
                RenderingDevice.DataFormat.R8G8B8A8Unorm, (uint)size.X, (uint)size.Y, handles[i]);
            if (!_textures[i].IsValid) return false;
        }
        _size = size;
        _transparent = transparent;
        return true;
    }

    private void FreeTextures()
    {
        // The engine's imports only; the view's textures live until Qt stops sampling them.
        foreach (var texture in _textures)
        {
            if (texture.IsValid) _rd.FreeRid(texture);
        }
        _textures = [];
        _size = default;
    }

    private void Fail()
    {
        _failed = true;
        Dispose();
    }

    public void Dispose()
    {
        Native.GpuDestroy(_view);
        FreeTextures();
    }
}
