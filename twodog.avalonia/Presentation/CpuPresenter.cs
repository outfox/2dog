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

        using var image = RenderingServer.Texture2DGet(_session.ViewportTexture);
        if (image is null) return;

        var width = image.GetWidth();
        var height = image.GetHeight();
        if (width <= 0 || height <= 0) return;
        var format = image.GetFormat();
        if (format != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
            // HDR 2D viewports read back as linear half-float; quantizing alone leaves the
            // linear values in an sRGB-interpreted bitmap, appearing far too dark. Convert
            // like Godot's own capture path (MovieWriter) does.
            if (format is Image.Format.Rgbah or Image.Format.Rgbaf) image.LinearToSrgb();
        }

        var pixelSize = new PixelSize(width, height);
        if (_back is null || _back.PixelSize != pixelSize)
        {
            _back?.Dispose();
            // Godot renders 2D onto the transparent target with premultiplied math (mix
            // blending onto transparent black), so the readback carries premultiplied
            // alpha; Unpremul would have Avalonia multiply by alpha a second time,
            // darkening semi-transparent content.
            _back = new WriteableBitmap(pixelSize, new Vector(96, 96),
                PixelFormats.Rgba8888, AlphaFormat.Premul);
        }

        var data = image.GetData();
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
