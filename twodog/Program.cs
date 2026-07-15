using System.Reflection;
using twodog.cli;

// Manual argument parsing, in the style of twodog.import: one verb, a handful
// of flags, zero dependencies.

var argv = new Queue<string>(args);

if (argv.Count == 0)
{
    PrintUsage();
    return 1;
}

var verb = argv.Dequeue();
switch (verb)
{
    case "--help" or "-h" or "help":
        PrintUsage();
        return 0;

    case "--version":
        Console.WriteLine(ToolVersions.TwoDogVersion);
        return 0;

    case "convert":
        break;

    default:
        Console.Error.WriteLine($"error: unknown command '{verb}'");
        PrintUsage();
        return 1;
}

var options = new ConvertOptions();
while (argv.Count > 0)
{
    var arg = argv.Dequeue();
    switch (arg)
    {
        case "--name":
            if (argv.Count == 0) return UsageError("--name requires a value");
            options.NameOverride = argv.Dequeue();
            break;
        case "--no-web":
            options.IncludeWeb = false;
            break;
        case "--no-tests":
            options.IncludeTests = false;
            break;
        case "--dry-run":
            options.DryRun = true;
            break;
        case "--force":
            options.Force = true;
            break;
        case "--no-restore":
            options.Restore = false;
            break;
        case "--verbose":
            options.Verbose = true;
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        default:
            if (arg.StartsWith('-')) return UsageError($"unknown option '{arg}'");
            if (options.ProjectPath != null) return UsageError("more than one project path given");
            options.ProjectPath = arg;
            break;
    }
}

try
{
    return ConvertCommand.Run(options);
}
catch (ConvertException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

static int UsageError(string message)
{
    Console.Error.WriteLine($"error: {message}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine(
        $"""
         2dog {ToolVersions.TwoDogVersion} - https://2dog.dev

         usage: 2dog convert [path] [options]

         Converts an existing Godot project to 2dog in place. The Godot project
         directory becomes the solution root; host projects are scaffolded as
         nested subfolders that the Godot editor ignores (.gdignore). No
         existing file is ever moved, renamed or deleted.

           path              Godot project directory (default: current directory;
                             must contain project.godot)
           --name <name>     Override the derived project base name
           --no-web          Skip the browser (WebAssembly) host project
           --no-tests        Skip the xUnit test project
           --dry-run         Print planned actions without changing anything
           --force           Overwrite files that already exist (never deletes/moves)
           --no-restore      Skip the final 'dotnet restore'
           --verbose         Extra output
         """);
}

namespace twodog.cli
{
    /// <summary>Versions baked in at build time from Directory.Build.props.</summary>
    internal static class ToolVersions
    {
        private static string Metadata(string key) =>
            typeof(ToolVersions).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value
            ?? throw new InvalidOperationException($"Assembly metadata '{key}' missing");

        public static string TwoDogVersion => Metadata("TwoDogVersion");
        public static string NativesVersion => Metadata("NativesVersion");
        public static string GodotSdkVersion => Metadata("GodotSdkVersion");
    }

    internal sealed class ConvertOptions
    {
        public string? ProjectPath;
        public string? NameOverride;
        public bool IncludeWeb = true;
        public bool IncludeTests = true;
        public bool DryRun;
        public bool Force;
        public bool Restore = true;
        public bool Verbose;
    }

    /// <summary>A conversion error with a user-facing message (exit code 2).</summary>
    internal sealed class ConvertException(string message) : Exception(message);
}
