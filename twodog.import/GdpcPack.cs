using System.Text;

namespace twodog.pck;

/// <summary>One file in a pack directory.</summary>
internal readonly record struct PckEntry(string Path, ulong Size);

/// <summary>A pack that could not be read: missing, not a pck, corrupt, or encrypted.</summary>
internal sealed class PckFormatException(string message) : Exception(message);

/// <summary>
/// Engine-free reader for the GDPC directory, mirroring PackedSourcePCK::try_open_pack (core/io/file_access_pack.cpp).
/// Shared between the twodog.import helper (--list-pack) and the 2dog tool (pack list).
/// </summary>
internal static class GdpcPack
{
    internal sealed record Listing(uint FormatVersion, uint Major, uint Minor, IReadOnlyList<PckEntry> Entries)
    {
        public ulong TotalBytes => Entries.Aggregate(0UL, (acc, e) => acc + e.Size);
    }

    public static Listing Read(string path)
    {
        if (!File.Exists(path))
            throw new PckFormatException($"Pack not found: {path}");

        using var f = new BinaryReader(File.OpenRead(path));
        if (f.ReadUInt32() != 0x43504447) // 'GDPC'
            throw new PckFormatException($"{path}: not a Godot .pck (bad magic; embedded packs are not supported)");

        var formatVersion = f.ReadUInt32();
        var major = f.ReadUInt32();
        var minor = f.ReadUInt32();
        f.ReadUInt32(); // patch
        if (formatVersion is < 2 or > 4)
            throw new PckFormatException($"{path}: unsupported pack format v{formatVersion}");

        var packFlags = f.ReadUInt32();
        var encryptedDirectory = (packFlags & 1) != 0; // PACK_DIR_ENCRYPTED
        if (encryptedDirectory)
            throw new PckFormatException($"{path}: pack directory is encrypted; cannot list.");

        f.ReadUInt64(); // file base
        if (formatVersion >= 3)
        {
            var directoryOffset = f.ReadUInt64();
            if (directoryOffset >= (ulong)f.BaseStream.Length)
                throw new PckFormatException($"{path}: corrupt pack (directory offset beyond end of file)");
            f.BaseStream.Seek((long)directoryOffset, SeekOrigin.Begin);
        }
        else
        {
            for (var j = 0; j < 16; j++) f.ReadUInt32(); // reserved
        }

        // Bound against the remaining stream so a corrupt directory yields tidy errors, not OOM.
        var remaining = f.BaseStream.Length - f.BaseStream.Position;
        var count = f.ReadUInt32();
        const int minEntryBytes = 4 + 8 + 8 + 16 + 4;
        if (count > remaining / minEntryBytes)
            throw new PckFormatException($"{path}: corrupt pack (file count {count} exceeds directory size)");

        var entries = new List<PckEntry>((int)count);
        for (var j = 0; j < count; j++)
        {
            var pathLength = f.ReadInt32();
            if (pathLength < 0 || pathLength > f.BaseStream.Length - f.BaseStream.Position)
                throw new PckFormatException($"{path}: corrupt pack (bad path length in entry {j})");
            var filePath = Encoding.UTF8.GetString(f.ReadBytes(pathLength)).TrimEnd('\0');
            f.ReadUInt64(); // offset
            var size = f.ReadUInt64();
            f.ReadBytes(16); // md5
            var flags = f.ReadUInt32();
            if ((flags & 2) != 0) continue; // PACK_FILE_REMOVAL
            entries.Add(new PckEntry(filePath, size));
        }

        return new Listing(formatVersion, major, minor, entries);
    }

    /// <summary>The listing format both front ends print: a summary line, then every file by size.</summary>
    public static void Print(string path, Listing listing, TextWriter output)
    {
        output.WriteLine($"{path}: pack format v{listing.FormatVersion} (Godot {listing.Major}.{listing.Minor}), " +
                         $"{listing.Entries.Count} file(s), {listing.TotalBytes / 1048576.0:F1} MiB content");
        foreach (var (filePath, size) in listing.Entries.OrderByDescending(e => e.Size))
            output.WriteLine($"{size,12:N0}  {filePath}");
    }

    /// <summary>List to stdout, errors to stderr: the helper's --list-pack exit contract.</summary>
    public static int ListToConsole(string path)
    {
        try
        {
            Print(path, Read(path), Console.Out);
            return 0;
        }
        catch (PckFormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
