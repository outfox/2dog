using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

string? editorPath = null;
string? libgodotPath = null;
string? apiDir = null;
string? toolsDir = null;
string? projectPath = null;
string? exportPreset = null;
string? exportOutput = null;
string? listPackPath = null;
var verbose = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--editor" when i + 1 < args.Length:
            editorPath = args[++i];
            break;
        case "--libgodot" when i + 1 < args.Length:
            libgodotPath = args[++i];
            break;
        case "--api-dir" when i + 1 < args.Length:
            apiDir = args[++i];
            break;
        case "--tools-dir" when i + 1 < args.Length:
            toolsDir = args[++i];
            break;
        case "--export-pack" when i + 1 < args.Length:
            exportPreset = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            exportOutput = args[++i];
            break;
        case "--list-pack" when i + 1 < args.Length:
            listPackPath = args[++i];
            break;
        case "--verbose":
            verbose = true;
            break;
        default:
            projectPath ??= args[i];
            break;
    }
}

editorPath ??= Environment.GetEnvironmentVariable("GODOT_EDITOR");
projectPath = projectPath != null ? Path.GetFullPath(projectPath) : null;
exportOutput = exportOutput != null ? Path.GetFullPath(exportOutput) : null;

// Pure file-format work ("why is my pck 99 MiB?"): no engine, no project.
if (listPackPath != null)
    return ListPack(listPackPath);

if (projectPath == null || !File.Exists(Path.Combine(projectPath, "project.godot")) ||
    (editorPath == null && libgodotPath == null) ||
    (exportPreset != null && exportOutput == null))
{
    Console.Error.WriteLine("Usage: twodog.import [--libgodot <libgodot-library>] [--editor <godot-binary>]");
    Console.Error.WriteLine("                     [--export-pack <preset> --output <pck-path>] <path-to-godot-project>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  --libgodot <path>  Path to an editor-variant libgodot shared library.");
    Console.Error.WriteLine("                     Runs the import in-process via libgodot_import_project.");
    Console.Error.WriteLine("  --api-dir <dir>    Directory containing GodotPlugins.dll (GODOTSHARP_DIR).");
    Console.Error.WriteLine("                     Defaults to the helper's own directory.");
    Console.Error.WriteLine("  --tools-dir <dir>  Directory containing GodotTools.dll (GODOT_TOOLS_DIR).");
    Console.Error.WriteLine("  --editor <path>    Path to a Godot editor binary; runs as a subprocess.");
    Console.Error.WriteLine("                     Falls back to the GODOT_EDITOR environment variable.");
    Console.Error.WriteLine("                     Takes precedence over --libgodot.");
    Console.Error.WriteLine("  --export-pack <preset>  Instead of importing, export the project's");
    Console.Error.WriteLine("                     content as a .pck using the named export preset");
    Console.Error.WriteLine("                     (from export_presets.cfg). Requires --output.");
    Console.Error.WriteLine("  --output <path>    Output .pck path for --export-pack.");
    Console.Error.WriteLine("  --list-pack <pck>  List a .pck's contents by size (no engine involved).");
    Console.Error.WriteLine("  --verbose          Pass --verbose to the engine.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  The project path must contain a project.godot file.");
    return 1;
}

// Serialize concurrent imports of the same project (parallel MSBuild nodes
// building multiple consumers of one game project).
Directory.CreateDirectory(Path.Combine(projectPath, ".godot"));
var lockPath = Path.Combine(projectPath, ".godot", "2dog.import.lock");
using var importLock = AcquireLock(lockPath, TimeSpan.FromSeconds(120));
if (importLock == null)
{
    Console.Error.WriteLine($"Timed out waiting for import lock: {lockPath}");
    return 1;
}

// An explicitly configured external editor wins: it is unambiguous user intent
// and the battle-tested path.
if (editorPath != null)
{
    if (!File.Exists(editorPath))
    {
        Console.Error.WriteLine($"Editor binary not found: {editorPath}");
        return 1;
    }

    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = editorPath,
            UseShellExecute = false,
        }
    };
    if (exportPreset != null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(exportOutput!)!);
        foreach (var a in new[] { "--headless", "--export-pack", exportPreset, exportOutput!, "--path", projectPath })
            process.StartInfo.ArgumentList.Add(a);
    }
    else
    {
        foreach (var a in new[] { "--headless", "--import", "--path", projectPath })
            process.StartInfo.ArgumentList.Add(a);
    }
    if (verbose) process.StartInfo.ArgumentList.Add("--verbose");

    process.Start();
    process.WaitForExit();
    return process.ExitCode;
}

// In-process mode: load the editor-variant libgodot and run the full
// `--headless --import` lifecycle via libgodot_import_project.
if (!File.Exists(libgodotPath))
{
    Console.Error.WriteLine($"libgodot library not found: {libgodotPath}");
    return 1;
}

apiDir = Path.GetFullPath(apiDir ?? AppContext.BaseDirectory);
if (!File.Exists(Path.Combine(apiDir, "GodotPlugins.dll")))
{
    Console.Error.WriteLine($"GodotPlugins.dll not found in API directory: {apiDir}");
    return 1;
}

// GodotTools is mandatory for editor-mode C# initialization; the engine
// hard-aborts (CRASH_COND) if it fails to load, so validate up front.
toolsDir = toolsDir != null ? Path.GetFullPath(toolsDir) : null;
if (toolsDir == null || !File.Exists(Path.Combine(toolsDir, "GodotTools.dll")))
{
    Console.Error.WriteLine($"GodotTools.dll not found in tools directory: {toolsDir ?? "<unset>"}");
    Console.Error.WriteLine("Pass --tools-dir pointing at the GodotSharp/Tools assemblies.");
    return 1;
}

SetEnv("GODOTSHARP_DIR", apiDir);
SetEnv("GODOT_TOOLS_DIR", toolsDir);

var lib = NativeLibrary.Load(Path.GetFullPath(libgodotPath!));
var exportName = exportPreset != null ? "libgodot_export_pack" : "libgodot_import_project";
if (!NativeLibrary.TryGetExport(lib, exportName, out var export))
{
    Console.Error.WriteLine($"{exportName} export not found in {libgodotPath} - is this libgodot too old?");
    return 1;
}

int rc;
unsafe
{
    var projectUtf8 = Encoding.UTF8.GetBytes(projectPath + "\0");
    var verboseUtf8 = "--verbose\0"u8.ToArray();
    fixed (byte* pProject = projectUtf8)
    fixed (byte* pVerbose = verboseUtf8)
    {
        var extra = stackalloc byte*[1] { pVerbose };
        if (exportPreset != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exportOutput!)!);
            var exportPack = (delegate* unmanaged[Cdecl]<byte*, byte*, byte*, int, byte**, int>)export;
            var presetUtf8 = Encoding.UTF8.GetBytes(exportPreset + "\0");
            var outputUtf8 = Encoding.UTF8.GetBytes(exportOutput + "\0");
            fixed (byte* pPreset = presetUtf8)
            fixed (byte* pOutput = outputUtf8)
            {
                rc = exportPack(pProject, pPreset, pOutput, verbose ? 1 : 0, verbose ? extra : null);
            }
        }
        else
        {
            var import = (delegate* unmanaged[Cdecl]<byte*, int, byte**, int>)export;
            rc = import(pProject, verbose ? 1 : 0, verbose ? extra : null);
        }
    }
}

if (rc == -1)
    Console.Error.WriteLine($"{libgodotPath} is not an editor build of libgodot; {(exportPreset != null ? "export" : "import")} requires the editor variant.");

// Engine cleanup can leave non-background threads; exit hard so the helper
// process reliably terminates.
importLock.Dispose();
Environment.Exit(rc);
return rc;

// .NET's Environment.SetEnvironmentVariable does not propagate to native
// getenv() on Linux/.NET 8+, and Godot's native code reads these variables.
static void SetEnv(string name, string value)
{
    Environment.SetEnvironmentVariable(name, value);
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        setenv(name, value, 1);
}

// GDPC directory layout, mirroring PackedSourcePCK::try_open_pack
// (core/io/file_access_pack.cpp): magic, format version, engine version
// triple, pack flags, file base; v3/v4 then carry a directory offset (plus a
// 32-byte salt for encrypted sparse bundles), v2 sixteen reserved u32s; the
// directory is count followed by (padded path, offset, size, md5, flags).
static int ListPack(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Pack not found: {path}");
        return 1;
    }

    using var f = new BinaryReader(File.OpenRead(path));
    if (f.ReadUInt32() != 0x43504447) // 'GDPC'
    {
        Console.Error.WriteLine($"{path}: not a Godot .pck (bad magic; embedded packs are not supported)");
        return 1;
    }

    var formatVersion = f.ReadUInt32();
    var major = f.ReadUInt32();
    var minor = f.ReadUInt32();
    f.ReadUInt32(); // patch
    if (formatVersion is < 2 or > 4)
    {
        Console.Error.WriteLine($"{path}: unsupported pack format v{formatVersion}");
        return 1;
    }

    var packFlags = f.ReadUInt32();
    var encryptedDirectory = (packFlags & 1) != 0; // PACK_DIR_ENCRYPTED
    var sparseBundle = (packFlags & 4) != 0;       // PACK_SPARSE_BUNDLE
    if (encryptedDirectory)
    {
        Console.Error.WriteLine($"{path}: pack directory is encrypted; cannot list.");
        return 1;
    }

    f.ReadUInt64(); // file base
    if (formatVersion >= 3)
    {
        var directoryOffset = f.ReadUInt64();
        if (sparseBundle && encryptedDirectory && formatVersion == 4) f.ReadBytes(32); // salt
        f.BaseStream.Seek((long)directoryOffset, SeekOrigin.Begin);
    }
    else
    {
        for (var j = 0; j < 16; j++) f.ReadUInt32(); // reserved
    }

    var count = f.ReadUInt32();
    var entries = new List<(string Path, ulong Size)>((int)count);
    for (var j = 0; j < count; j++)
    {
        var pathLength = f.ReadInt32();
        var filePath = Encoding.UTF8.GetString(f.ReadBytes(pathLength)).TrimEnd('\0');
        f.ReadUInt64(); // offset
        var size = f.ReadUInt64();
        f.ReadBytes(16); // md5
        var flags = f.ReadUInt32();
        if ((flags & 2) != 0) continue; // PACK_FILE_REMOVAL
        entries.Add((filePath, size));
    }

    var totalBytes = entries.Aggregate(0UL, (acc, e) => acc + e.Size);
    Console.WriteLine($"{path}: pack format v{formatVersion} (Godot {major}.{minor}), " +
                      $"{entries.Count} file(s), {totalBytes / 1048576.0:F1} MiB content");
    foreach (var (filePath, size) in entries.OrderByDescending(e => e.Size))
        Console.WriteLine($"{size,12:N0}  {filePath}");
    return 0;
}

static FileStream? AcquireLock(string path, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Thread.Sleep(500);
        }
    }

    return null;
}

[DllImport("libc", SetLastError = true)]
static extern int setenv(string name, string value, int overwrite);
