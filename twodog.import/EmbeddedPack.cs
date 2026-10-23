using System;
using System.IO;
using System.Text;

namespace twodog.pck;

/// <summary>
/// Packs inside executables, where PackedSourcePCK::try_open_pack (core/io/file_access_pack.cpp) looks for them: a "pck"
/// PE section on Windows, which still resolves after Authenticode signing, and the GDPC end-of-file trailer elsewhere.
/// Shared by twodog.import (embedding), twodog.engine (Engine.ResolveContent) and the 2dog tool (pack list).
/// </summary>
internal static class EmbeddedPack
{
    private const uint Magic = 0x43504447; // 'GDPC'
    private const uint PeSignature = 0x00004550; // 'PE\0\0'
    private const uint SectionFlags = 0x40000040; // IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ

    /// <summary>Offset of the pack header: 0 for a standalone pack, -1 when the file holds none.</summary>
    public static long Find(Stream file)
    {
        if (file.Length < 4) return -1;
        using var r = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
        file.Position = 0;
        var head = r.ReadUInt32();
        if (head == Magic) return 0;

        if ((head & 0xFFFF) == 0x5A4D && PeSection(file, r, "pck") is { } section)
        {
            // Godot allows up to 7 bytes between the section start and the header.
            for (var i = 0; i < 8 && section + i + 4 <= file.Length; i++)
            {
                file.Position = section + i;
                if (r.ReadUInt32() == Magic) return section + i;
            }
        }

        if (file.Length < 16) return -1;
        file.Position = file.Length - 4;
        if (r.ReadUInt32() != Magic) return -1;
        file.Position = file.Length - 12;
        var start = file.Length - 12 - r.ReadInt64();
        if (start < 0 || start > file.Length - 4) return -1;
        file.Position = start;
        return r.ReadUInt32() == Magic ? start : -1;
    }

    /// <summary>Whether the executable carries an embedded pack; false when it cannot be read.</summary>
    public static bool Contains(string executablePath)
    {
        try
        {
            using var file = File.OpenRead(executablePath);
            return Find(file) > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Appends <paramref name="pckPath"/> to the executable: as a "pck" section of a PE image (sign afterwards), or
    /// followed by the GDPC trailer for ELF. Mach-O executables carry their pack in the .app's Resources instead.
    /// </summary>
    public static void Embed(string executablePath, string pckPath)
    {
        using var pck = File.OpenRead(pckPath);
        RequireRelocatablePack(pck, pckPath);

        using var exe = new FileStream(executablePath, FileMode.Open, FileAccess.ReadWrite);
        if (Find(exe) >= 0)
            throw new InvalidOperationException($"{executablePath} already contains a Godot pack.");

        using var r = new BinaryReader(exe, Encoding.UTF8, leaveOpen: true);
        exe.Position = 0;
        var head = r.ReadUInt32();
        pck.Position = 0;
        if ((head & 0xFFFF) == 0x5A4D)
            EmbedPe(exe, r, pck, executablePath);
        else if (head == 0x464C457F) // '\x7FELF'
            AppendTrailer(exe, pck);
        else
            throw new InvalidOperationException(
                $"{executablePath} is neither a PE nor an ELF executable; macOS apps carry their pack in Contents/Resources.");
    }

    // Embedded packs are read relative to their own start: format v3+ always, v2 only with PACK_REL_FILEBASE.
    private static void RequireRelocatablePack(Stream pck, string path)
    {
        using var r = new BinaryReader(pck, Encoding.UTF8, leaveOpen: true);
        if (pck.Length < 24 || r.ReadUInt32() != Magic)
            throw new InvalidOperationException($"{path} is not a Godot .pck.");
        var version = r.ReadUInt32();
        pck.Position = 20;
        var flags = r.ReadUInt32();
        if (version < 3 && (flags & 2) == 0)
            throw new InvalidOperationException($"{path} uses absolute file offsets (pack format v{version}) and cannot be embedded.");
    }

    private static void AppendTrailer(FileStream exe, Stream pck)
    {
        exe.Position = exe.Length;
        var start = exe.Position;
        pck.CopyTo(exe);
        using var w = new BinaryWriter(exe, Encoding.UTF8, leaveOpen: true);
        w.Write(exe.Position - start);
        w.Write(Magic);
    }

    // Adds two sections after the image's own: one covering the existing overlay (the .NET single-file bundle), so
    // Authenticode hashes it, and the "pck" section Godot looks up.
    private static void EmbedPe(FileStream exe, BinaryReader r, Stream pck, string path)
    {
        exe.Position = 0x3C;
        var pe = r.ReadInt32();
        exe.Position = pe;
        if (r.ReadUInt32() != PeSignature) throw new InvalidOperationException($"{path}: invalid PE header.");
        var coff = pe + 4;
        exe.Position = coff + 2;
        var sectionCount = r.ReadUInt16();
        exe.Position = coff + 16;
        var optionalSize = r.ReadUInt16();
        var optional = coff + 20;
        exe.Position = optional;
        var dataDirectories = optional + (r.ReadUInt16() == 0x20B ? 112 : 96);
        exe.Position = optional + 32;
        var sectionAlignment = r.ReadUInt32();
        var fileAlignment = r.ReadUInt32();
        exe.Position = optional + 60;
        var headersSize = r.ReadUInt32();

        exe.Position = dataDirectories + 4 * 8 + 4;
        if (r.ReadUInt32() != 0)
            throw new InvalidOperationException($"{path} is already signed: embed the pack first, then sign the executable.");
        exe.Position = dataDirectories + 11 * 8 + 4; // bound imports live in the header slack the new sections need
        if (r.ReadUInt32() != 0)
            throw new InvalidOperationException($"{path} has bound imports in its headers; cannot add sections.");

        var table = optional + optionalSize;
        long rawEnd = 0, firstRaw = long.MaxValue, imageEnd = 0;
        for (var i = 0; i < sectionCount; i++)
        {
            exe.Position = table + i * 40 + 8;
            var virtualSize = r.ReadUInt32();
            var virtualAddress = r.ReadUInt32();
            var rawSize = r.ReadUInt32();
            var rawPointer = r.ReadUInt32();
            if (rawSize > 0) firstRaw = Math.Min(firstRaw, rawPointer);
            rawEnd = Math.Max(rawEnd, (long)rawPointer + rawSize);
            imageEnd = Math.Max(imageEnd, (long)virtualAddress + Math.Max(virtualSize, rawSize));
        }

        var overlay = exe.Length - rawEnd;
        var added = overlay > 0 ? 2 : 1;
        if (table + (sectionCount + added) * 40 > Math.Min(headersSize, firstRaw))
            throw new InvalidOperationException($"{path} has no room for {added} more section header(s).");
        if (overlay > 0 && rawEnd % fileAlignment != 0)
            throw new InvalidOperationException($"{path}: data after the last section is not file-aligned.");

        exe.SetLength(Align(exe.Length, fileAlignment));
        var pckOffset = exe.Length;
        exe.Position = pckOffset;
        pck.CopyTo(exe);
        var pckSize = exe.Length - pckOffset;
        exe.SetLength(Align(exe.Length, fileAlignment));
        if (exe.Length > uint.MaxValue) throw new InvalidOperationException($"{path} would exceed 4 GiB.");

        using var w = new BinaryWriter(exe, Encoding.UTF8, leaveOpen: true);
        var address = Align(imageEnd, sectionAlignment);
        var index = (int)sectionCount;
        if (overlay > 0) address = WriteSection(".bundle", rawEnd, overlay);
        address = WriteSection("pck", pckOffset, pckSize);

        exe.Position = coff + 2;
        w.Write((ushort)index);
        exe.Position = optional + 56; // SizeOfImage
        w.Write((uint)address);
        exe.Position = optional + 64; // CheckSum: only drivers verify it
        w.Write(0u);
        return;

        long WriteSection(string name, long offset, long size)
        {
            exe.Position = table + index++ * 40;
            var nameBytes = new byte[8];
            Encoding.ASCII.GetBytes(name, nameBytes);
            w.Write(nameBytes);
            w.Write((uint)size);
            w.Write((uint)address);
            w.Write((uint)Align(size, fileAlignment));
            w.Write((uint)offset);
            w.Write(0u); // relocations
            w.Write(0u); // line numbers
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write(SectionFlags);
            return Align(address + size, sectionAlignment);
        }
    }

    private static long? PeSection(Stream file, BinaryReader r, string name)
    {
        if (file.Length < 0x40) return null;
        file.Position = 0x3C;
        var pe = r.ReadInt32();
        if (pe < 0 || pe + 24 > file.Length) return null;
        file.Position = pe;
        if (r.ReadUInt32() != PeSignature) return null;
        file.Position = pe + 4 + 2;
        var count = r.ReadUInt16();
        file.Position = pe + 4 + 16;
        var table = pe + 4 + 20 + r.ReadUInt16();
        for (var i = 0; i < count && table + i * 40 + 24 <= file.Length; i++)
        {
            file.Position = table + i * 40;
            var sectionName = Encoding.ASCII.GetString(r.ReadBytes(8)).TrimEnd('\0');
            if (sectionName != name) continue;
            file.Position = table + i * 40 + 20;
            return r.ReadUInt32();
        }

        return null;
    }

    private static long Align(long value, long alignment) => (value + alignment - 1) / alignment * alignment;
}
