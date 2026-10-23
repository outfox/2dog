using System.Reflection.PortableExecutable;
using twodog.pck;

namespace twodog.tests.ToolTests;

/// <summary>Packs embedded the way Godot's PackedSourcePCK::try_open_pack looks for them.</summary>
[Trait("Precision", "Independent")]
public class EmbeddedPackTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("twodog-embed-").FullName;
    private readonly byte[] _pck = PackToolTests.BuildPck(4u, 2u, ("res://a.tscn", 10, 0u), ("res://b.png", 20, 0u));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A PE32+ image with one section and <paramref name="overlay"/> bytes after it (a single-file bundle).</summary>
    private static byte[] BuildPe(byte[] overlay, uint certificateSize = 0)
    {
        var image = new byte[0x600];
        var w = new BinaryWriter(new MemoryStream(image));
        w.Write((ushort)0x5A4D);
        w.Seek(0x3C, SeekOrigin.Begin); w.Write(0x40);
        w.Seek(0x40, SeekOrigin.Begin); w.Write(0x00004550u);
        w.Write((ushort)0x8664); w.Write((ushort)1); w.Write(new byte[12]); w.Write((ushort)240); w.Write((ushort)0x22);
        var optional = 0x58;
        w.Write((ushort)0x20B);
        w.Seek(optional + 32, SeekOrigin.Begin); w.Write(0x1000u); w.Write(0x200u);
        w.Seek(optional + 56, SeekOrigin.Begin); w.Write(0x2000u); w.Write(0x400u);
        w.Seek(optional + 108, SeekOrigin.Begin); w.Write(16u);
        w.Seek(optional + 112 + 4 * 8 + 4, SeekOrigin.Begin); w.Write(certificateSize);
        w.Seek(optional + 240, SeekOrigin.Begin);
        w.Write(".text\0\0\0"u8.ToArray()); w.Write(0x200u); w.Write(0x1000u); w.Write(0x200u); w.Write(0x400u);
        return [.. image, .. overlay];
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void PeGetsSectionsCoveringTheBundleAndThePack()
    {
        var bundle = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        var exe = Write("game.exe", BuildPe(bundle));
        EmbeddedPack.Embed(exe, Write("game.pck", _pck));

        var bytes = File.ReadAllBytes(exe);
        Assert.Equal(bundle, bytes[0x600..(0x600 + bundle.Length)]); // the bundle's absolute offsets still hold
        using var pe = new PEReader(new MemoryStream(bytes));
        var sections = pe.PEHeaders.SectionHeaders;
        Assert.Equal(new[] { ".text", ".bundle", "pck" }, sections.Select(s => s.Name));
        Assert.Equal(0x600, sections[1].PointerToRawData);
        Assert.Equal(bundle.Length, sections[1].VirtualSize);
        Assert.Equal(sections[2].PointerToRawData + sections[2].SizeOfRawData, bytes.Length); // nothing left unhashed
        Assert.Equal((uint)(sections[2].VirtualAddress + 0x1000), (uint)pe.PEHeaders.PEHeader!.SizeOfImage);

        using (var file = File.OpenRead(exe))
            Assert.Equal(sections[2].PointerToRawData, EmbeddedPack.Find(file));
        Assert.True(EmbeddedPack.Contains(exe));
        Assert.Equal(new[] { "res://b.png", "res://a.tscn" }, GdpcPack.Read(exe).Entries.OrderByDescending(e => e.Size).Select(e => e.Path));
    }

    [Fact]
    public void ElfGetsTheGodotTrailer()
    {
        byte[] elf = [0x7F, (byte)'E', (byte)'L', (byte)'F', .. new byte[100]];
        var exe = Write("game", elf);
        EmbeddedPack.Embed(exe, Write("game.pck", _pck));

        using (var file = File.OpenRead(exe))
            Assert.Equal(elf.Length, EmbeddedPack.Find(file));
        Assert.Equal(2, GdpcPack.Read(exe).Entries.Count);
        Assert.Throws<InvalidOperationException>(() => EmbeddedPack.Embed(exe, Path.Combine(_dir, "game.pck")));
    }

    [Fact]
    public void SignedPeIsRefused()
    {
        var exe = Write("signed.exe", BuildPe([], certificateSize: 0x100));
        var error = Assert.Throws<InvalidOperationException>(() => EmbeddedPack.Embed(exe, Write("game.pck", _pck)));
        Assert.Contains("then sign", error.Message);
        Assert.False(EmbeddedPack.Contains(exe));
    }
}
