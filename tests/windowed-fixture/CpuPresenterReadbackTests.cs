using Godot;
using twodog.Presentation;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.Windowed;

[Collection<RenderingCollection>]
public class CpuPresenterReadbackTests(Fixture godot)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeDimensionalBackgroundRespectsViewportTransparency(bool transparent)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(8, 6),
            TransparentBg = transparent,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        using var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.118f, 0.149f, 0.196f),
        };
        viewport.AddChild(new WorldEnvironment { Environment = environment });
        viewport.AddChild(new Camera3D());
        godot.Tree.Root.AddChild(viewport);
        try
        {
            for (var frame = 0; frame < 4; frame++) godot.Engine.Update();
            var texture = viewport.GetTexture().GetRid();
            using var image = RenderingServer.Texture2DGet(texture);
            image.Convert(Image.Format.Rgba8);
            var pixels = CpuPresenter.ReadPixels(texture, transparent, out _, out _);
            Assert.NotNull(pixels);
            Assert.Equal(transparent ? 0 : 255, pixels[3]);
            Assert.Equal(image.GetData(), pixels);
        }
        finally
        {
            viewport.Free();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PixelsMatchImageReadbackAcrossResize(bool transparent, bool hdr)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(8, 6),
            TransparentBg = transparent,
            UseHdr2D = hdr,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var color = new ColorRect { Color = new Color(0.8f, 0.4f, 0.2f, 0.5f) };
        viewport.AddChild(color);
        godot.Tree.Root.AddChild(viewport);
        try
        {
            foreach (var size in new[] { new Vector2I(8, 6), new Vector2I(7, 5) })
            {
                viewport.Size = size;
                color.Size = new Vector2(size.X, size.Y);
                for (var frame = 0; frame < 4; frame++) godot.Engine.Update();

                var texture = viewport.GetTexture().GetRid();
                using var image = RenderingServer.Texture2DGet(texture);
                Assert.NotNull(image);
                var format = image.GetFormat();
                image.Convert(Image.Format.Rgba8);
                if (format is Image.Format.Rgbah or Image.Format.Rgbaf) image.LinearToSrgb();

                var pixels = CpuPresenter.ReadPixels(texture, transparent, out var width, out var height);
                Assert.Equal(size.X, width);
                Assert.Equal(size.Y, height);
                Assert.Equal(image.GetData(), pixels);
            }
        }
        finally
        {
            viewport.Free();
        }
    }
}
