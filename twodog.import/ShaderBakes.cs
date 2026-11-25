using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace twodog.pck;

internal static class ShaderBakes
{
    public static int Export(string[] args, string selection, string output)
    {
        var methods = selection.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        if (methods.Any(m => m is not ("forward_plus" or "mobile" or "gl_compatibility" or "none")) ||
            (methods.Contains("none") && methods.Length != 1))
            throw new ArgumentException("TwoDogBakeShaders accepts forward_plus, mobile, gl_compatibility, or none on its own.");

        if (methods.Contains("gl_compatibility"))
            Console.WriteLine("2dog: gl_compatibility compiles shaders at runtime.");
        var workerArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--bake-shaders" or "--rendering-method" or "--rendering-driver" or "--output") { i++; continue; }
            workerArgs.Add(args[i]);
        }

        var scratch = Directory.CreateTempSubdirectory("2dog-shaders-");
        try
        {
            foreach (string method in methods.Where(m => m is "forward_plus" or "mobile"))
            {
                var archive = Path.Combine(scratch.FullName, method + ".zip");
                int rc = Run(workerArgs, archive, method, scratch.FullName);
                if (rc != 0) return rc;
                if (File.Exists(archive + ".skip"))
                {
                    Console.WriteLine("2dog: " + File.ReadAllText(archive + ".skip"));
                    File.Delete(archive);
                    break;
                }
                using var zip = ZipFile.OpenRead(archive);
                var family = method == "mobile" ? "SceneForwardMobileShaderRD" : "SceneForwardClusteredShaderRD";
                if (!zip.Entries.Any(e => e.FullName.StartsWith(".godot/shader_cache/" + family + "/", StringComparison.Ordinal) &&
                                          e.FullName.EndsWith(".cache", StringComparison.Ordinal)))
                    throw new InvalidDataException($"No {method} shader caches in the export. Check the editor, target driver and shader baker.");
            }

            int result = Run(workerArgs, output, null, scratch.FullName);
            if (result == 0 && Directory.EnumerateFiles(scratch.FullName, "*.zip").Any() &&
                !File.Exists(Path.Combine(scratch.FullName, "packed")))
                throw new InvalidDataException("The editor did not pack the shader archives. Use matching 2dog editor tools.");
            return result;
        }
        finally
        {
            scratch.Delete(true);
        }
    }

    private static int Run(List<string> args, string output, string? method, string directory)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(output);
        start.Environment["TWODOG_SHADER_BAKE_DIR"] = directory;
        if (method != null)
        {
            start.Environment["TWODOG_SHADER_BAKE_METHOD"] = method;
            start.ArgumentList.Add("--rendering-method");
            start.ArgumentList.Add(method);
            start.ArgumentList.Add("--rendering-driver");
            start.ArgumentList.Add(OperatingSystem.IsMacOS() ? "metal" : "vulkan");
        }
        else start.Environment.Remove("TWODOG_SHADER_BAKE_METHOD");
        using var child = Process.Start(start)!;
        child.WaitForExit();
        return child.ExitCode;
    }
}
