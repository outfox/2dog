using System.Text;
using twodog.cli;
using twodog.pck;

namespace twodog.tests.ToolTests;

/// <summary>
/// The `2dog pack list` verb and the GDPC reader behind it (shared with the
/// twodog.import helper's --list-pack): synthetic packs only, no engine.
/// </summary>
public class PackToolTests
{
    /// <summary>A minimal GDPC pack: v2 uses the reserved block, v3/v4 a directory offset.</summary>
    private static byte[] BuildPck(uint formatVersion, uint packFlags,
        params (string Path, ulong Size, uint Flags)[] entries)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x43504447u); // 'GDPC'
        w.Write(formatVersion);
        w.Write(4u); w.Write(7u); w.Write(1u); // engine version triple
        w.Write(packFlags);
        w.Write(0UL); // file base
        var offsetField = -1L;
        if (formatVersion >= 3)
        {
            offsetField = ms.Position;
            w.Write(0UL); // directory offset, patched below
        }
        else
        {
            for (var i = 0; i < 16; i++) w.Write(0u); // reserved
        }

        var directory = ms.Position;
        w.Write((uint)entries.Length);
        foreach (var (path, size, flags) in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(path);
            w.Write(bytes.Length);
            w.Write(bytes);
            w.Write(0UL); // offset
            w.Write(size);
            w.Write(new byte[16]); // md5
            w.Write(flags);
        }

        if (offsetField >= 0)
        {
            ms.Position = offsetField;
            w.Write((ulong)directory);
        }

        return ms.ToArray();
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(4u)]
    public void Read_ListsEntriesAndSkipsRemovals(uint formatVersion)
    {
        using var tmp = new TempProjectDir();
        var pck = tmp.Write("game.pck", BuildPck(formatVersion, 0,
            ("res://scene.tscn", 100, 0),
            ("res://big.res", 5000, 0),
            ("res://gone.res", 7, 2))); // PACK_FILE_REMOVAL

        var listing = GdpcPack.Read(pck);

        Assert.Equal(formatVersion, listing.FormatVersion);
        Assert.Equal((4u, 7u), (listing.Major, listing.Minor));
        Assert.Equal(["res://scene.tscn", "res://big.res"], listing.Entries.Select(e => e.Path).ToArray());
        Assert.Equal(5100UL, listing.TotalBytes);

        var output = new StringWriter();
        GdpcPack.Print(pck, listing, output);
        var lines = output.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Contains($"pack format v{formatVersion} (Godot 4.7), 2 file(s)", lines[0]);
        // Largest first, and the removed entry never shows.
        Assert.Contains("res://big.res", lines[1]);
        Assert.Contains("res://scene.tscn", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Read_RejectsMissingCorruptAndEncryptedPacks()
    {
        using var tmp = new TempProjectDir();

        var missing = Path.Combine(tmp.Dir, "missing.pck");
        Assert.Contains("Pack not found", Assert.Throws<PckFormatException>(() => GdpcPack.Read(missing)).Message);

        var notAPck = tmp.Write("nope.pck", "just text, definitely not GDPC bytes"u8.ToArray());
        Assert.Contains("bad magic", Assert.Throws<PckFormatException>(() => GdpcPack.Read(notAPck)).Message);

        var futuristic = tmp.Write("v5.pck", BuildPck(5, 0));
        Assert.Contains("unsupported pack format v5",
            Assert.Throws<PckFormatException>(() => GdpcPack.Read(futuristic)).Message);

        var encrypted = tmp.Write("enc.pck", BuildPck(2, 1)); // PACK_DIR_ENCRYPTED
        Assert.Contains("encrypted", Assert.Throws<PckFormatException>(() => GdpcPack.Read(encrypted)).Message);

        // v2 header is fixed-size, so the directory count lives at byte 96;
        // an absurd count must fail the bounds check, not allocate or throw raw.
        var corrupt = BuildPck(2, 0, ("res://a.res", 1, 0));
        BitConverter.GetBytes(uint.MaxValue).CopyTo(corrupt, 96);
        var corruptPck = tmp.Write("corrupt.pck", corrupt);
        Assert.Contains("exceeds directory size",
            Assert.Throws<PckFormatException>(() => GdpcPack.Read(corruptPck)).Message);
    }

    [Fact]
    public void PackVerb_ParsesListsAndReportsErrors()
    {
        using var tmp = new TempProjectDir();
        var pck = tmp.Write("game.pck", BuildPck(4, 0, ("res://scene.tscn", 100, 0)));

        Assert.Equal(Verb.Pack, CommandLine.Parse(["pack", "list", pck]).Verb);
        Assert.Equal(pck, CommandLine.Parse(["pack", "list", pck]).PackFile);

        Assert.Equal(0, Program.Main(["pack", "list", pck]));

        // Usage errors exit 1, unreadable packs exit 2 (ToolException).
        Assert.Equal(1, Program.Main(["pack"]));
        Assert.Equal(1, Program.Main(["pack", "list"]));
        Assert.Equal(1, Program.Main(["pack", "extract", pck]));
        Assert.Equal(1, Program.Main(["pack", "list", pck, "extra"]));
        Assert.Equal(2, Program.Main(["pack", "list", Path.Combine(tmp.Dir, "missing.pck")]));
    }
}
