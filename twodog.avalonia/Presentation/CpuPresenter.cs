using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Godot;
using Image = Godot.Image;

namespace twodog.Presentation;

/// <summary>
/// Universal fallback presenter: reads the main viewport back to the CPU each frame and hands
/// the control a WriteableBitmap. Costs a GPU-to-CPU-to-GPU round trip per frame; the zero-copy
/// GPU presenter replaces this wherever the natives support texture sharing.
/// </summary>
internal sealed class CpuPresenter : IPresenter
{
    private readonly GodotControl _control;
    private readonly GodotSession _session;

    // Two bitmaps ping-pong: Avalonia's render thread may still read the presented one
    // while the next frame is written.
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;

    public CpuPresenter(GodotControl control, GodotSession session)
    {
        _control = control;
        _session = session;
    }

    public GodotPresentationMode Mode => GodotPresentationMode.Cpu;

    public bool Ready => true;

    public bool Failed => false;

    public void PresentFrame()
    {
        if (!_session.IsStarted) return;

        var data = ReadPixels(_session.ViewportTexture, _session.Engine.Tree.Root.TransparentBg,
            out var width, out var height);
        if (data is null || data.Length == 0 || width <= 0 || height <= 0) return;

        var pixelSize = new PixelSize(width, height);
        if (_back is null || _back.PixelSize != pixelSize)
        {
            _back?.Dispose();
            // Transparent targets carry premultiplied color; do not multiply their alpha again.
            _back = new WriteableBitmap(pixelSize, new Vector(96, 96),
                PixelFormats.Rgba8888, AlphaFormat.Premul);
        }

        using (var fb = _back.Lock())
        {
            var sourceStride = width * 4;
            if (fb.RowBytes == sourceStride)
            {
                Marshal.Copy(data, 0, fb.Address, sourceStride * height);
            }
            else
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(data, y * sourceStride, fb.Address + y * fb.RowBytes, sourceStride);
            }
        }

        (_front, _back) = (_back, _front);
        _control.PresentedFrame = _front;
        _control.InvalidateVisual();
    }

    internal static byte[]? ReadPixels(Rid viewportTexture, bool transparent, out int width, out int height)
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is not null)
        {
            var texture = RenderingServer.TextureGetRdTexture(viewportTexture);
            if (texture.IsValid)
            {
                using var format = rd.TextureGetFormat(texture);
                if (format.Format is RenderingDevice.DataFormat.R8G8B8A8Unorm or RenderingDevice.DataFormat.R8G8B8A8Srgb)
                {
                    // Texture2DGet strips opaque alpha; avoid RGBA -> RGB -> RGBA conversion every frame.
                    width = (int)format.Width;
                    height = (int)format.Height;
                    var data = rd.TextureGetData(texture, 0);
                    // Opaque 3D targets leave alpha unused; match Texture2DGet's opaque RGB readback.
                    if (!transparent)
                        for (var alpha = 3; alpha < data.Length; alpha += 4) data[alpha] = 255;
                    return data;
                }
            }
        }
        using var image = RenderingServer.Texture2DGet(viewportTexture);
        width = image?.GetWidth() ?? 0;
        height = image?.GetHeight() ?? 0;
        if (image is null || width <= 0 || height <= 0) return null;
        var formatImage = image.GetFormat();
        if (formatImage != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
            // HDR viewport readback is linear; Avalonia interprets the bitmap as sRGB.
            if (formatImage is Image.Format.Rgbah or Image.Format.Rgbaf) image.LinearToSrgb();
        }
        return image.GetData();
    }

    public void Resize()
    {
        // Nothing presenter-specific: the next readback picks up the resized viewport.
    }

    public void Dispose()
    {
        _control.PresentedFrame = null;
        _control.InvalidateVisual();
        _front?.Dispose();
        _back?.Dispose();
        _front = _back = null;
    }
}
