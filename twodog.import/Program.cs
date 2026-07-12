using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

string? editorPath = null;
string? libgodotPath = null;
string? apiDir = null;
string? toolsDir = null;
string? projectPath = null;
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

if (projectPath == null || !File.Exists(Path.Combine(projectPath, "project.godot")) ||
    (editorPath == null && libgodotPath == null))
{
    Console.Error.WriteLine("Usage: twodog.import [--libgodot <libgodot-library>] [--editor <godot-binary>] <path-to-godot-project>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  --libgodot <path>  Path to an editor-variant libgodot shared library.");
    Console.Error.WriteLine("                     Runs the import in-process via libgodot_import_project.");
    Console.Error.WriteLine("  --api-dir <dir>    Directory containing GodotPlugins.dll (GODOTSHARP_DIR).");
    Console.Error.WriteLine("                     Defaults to the helper's own directory.");
    Console.Error.WriteLine("  --tools-dir <dir>  Directory containing GodotTools.dll (GODOT_TOOLS_DIR).");
    Console.Error.WriteLine("  --editor <path>    Path to a Godot editor binary; runs the import as a");
    Console.Error.WriteLine("                     subprocess. Falls back to the GODOT_EDITOR environment");
    Console.Error.WriteLine("                     variable. Takes precedence over --libgodot.");
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
            ArgumentList = { "--headless", "--import", "--path", projectPath },
            UseShellExecute = false,
        }
    };
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
if (!NativeLibrary.TryGetExport(lib, "libgodot_import_project", out var export))
{
    Console.Error.WriteLine($"libgodot_import_project export not found in {libgodotPath} - is this libgodot too old?");
    return 1;
}

int rc;
unsafe
{
    var import = (delegate* unmanaged[Cdecl]<byte*, int, byte**, int>)export;
    var projectUtf8 = Encoding.UTF8.GetBytes(projectPath + "\0");
    var verboseUtf8 = "--verbose\0"u8.ToArray();
    fixed (byte* pProject = projectUtf8)
    fixed (byte* pVerbose = verboseUtf8)
    {
        var extra = stackalloc byte*[1] { pVerbose };
        rc = import(pProject, verbose ? 1 : 0, verbose ? extra : null);
    }
}

if (rc == -1)
    Console.Error.WriteLine($"{libgodotPath} is not an editor build of libgodot; import requires the editor variant.");

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
