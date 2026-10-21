using Godot;
using Image = Godot.Image;

namespace twodog.Presentation;

/// <summary>Reads each frame back to the CPU and hands the pixels to the view, which uploads them to Qt.</summary>
internal sealed unsafe class CpuPresenter(nint view) : IPresenter
{
    public QtPresentationMode Mode => QtPresentationMode.Cpu;

    public bool Failed => false;

    public void Present(Rid viewportTexture)
    {
        using var image = RenderingServer.Texture2DGet(viewportTexture);
        if (image is null || image.GetWidth() <= 0 || image.GetHeight() <= 0) return;
        var format = image.GetFormat();
        if (format != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
            // HDR 2D viewports read back linear; convert like Godot's own capture path (MovieWriter).
            if (format is Image.Format.Rgbah or Image.Format.Rgbaf) image.LinearToSrgb();
        }
        var data = image.GetData();
        fixed (byte* pixels = data)
            Native.ViewPresent(view, pixels, image.GetWidth(), image.GetHeight());
    }

    public void Dispose()
    {
    }
}
