using Godot;

internal static class TextureSwizzleSmoke
{
    public static Image CreateSource(Image.Format format, bool mipmaps = false)
    {
        byte[] data = new byte[mipmaps ? 48 : 16];
        for (int i = 0; i < data.Length; i += 16)
        {
            data[i] = (byte)(64 + i * 4);
            if (format == Image.Format.Dxt5RaAsRg)
            {
                data[i + 1] = data[i];
                data[i + 8] = data[i + 10] = 0;
                data[i + 9] = data[i + 11] = 0xf8;
            }
        }
        return Image.CreateFromData(4, 4, mipmaps, format, data);
    }

    public static void Run()
    {
        foreach (var format in new[] { Image.Format.Dxt5RaAsRg, Image.Format.Etc2RaAsRg, Image.Format.Rgba4444, Image.Format.Rgb565 })
        {
            using var source = format is Image.Format.Dxt5RaAsRg or Image.Format.Etc2RaAsRg
                ? CreateSource(format, true) : Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            using var expected = (Image)source.Duplicate();
            if (source.IsCompressed())
            {
                if (expected.Decompress() != Error.Ok) throw new Exception($"Could not decode {format} fixture.");
            }
            else
            {
                source.Fill(new Color(1, 0.25f, 0.125f, 0.75f));
                source.Convert(format);
                expected.CopyFrom(source);
                expected.Convert(Image.Format.Rgba8);
            }
            expected.Convert(Image.Format.Rgba8);
            var wanted = expected.GetData();
            using var texture = ImageTexture.CreateFromImage(source);
            using var restored = texture.GetImage();
            restored.Convert(Image.Format.Rgba8);
            var actual = restored.GetData();
            int tolerance = format is Image.Format.Rgb565 or Image.Format.Rgba4444 ? 8 : 0;
            if (actual.Length != wanted.Length || actual.Where((value, i) => Math.Abs(value - wanted[i]) > tolerance).Any())
                throw new Exception($"{format} lost its channel mapping or mipmaps.");
        }
        Console.WriteLine("NATIVE_TEXTURE_SWIZZLES_AND_MIPMAPS_PASSED");
    }
}
