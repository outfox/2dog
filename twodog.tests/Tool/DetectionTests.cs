using System.Text;
using twodog.cli;

namespace twodog.tests.ToolTests;

// Host recognition and project.godot handling on the awkward inputs real projects have: namespaces, comments,
// broken XML, CRLF files with byte-order marks, escaped values.
public class DetectionTests
{
    [Fact]
    public void Classify_RecognizesEveryTemplateHost()
    {
        foreach (var kind in Hosts.All)
        {
            var folder = $"Game.{Hosts.Suffix(kind)}";
            var csproj = TemplateAssets.HostFiles(kind, "Game", folder)
                .Single(f => f.RelativePath == $"{folder}/{folder}.csproj");

            Assert.Equal(kind, HostScan.Classify(csproj.Text, "renamed.folder"));
        }
    }

    [Fact]
    public void Classify_HandlesNamespacedProjects()
    {
        const string csproj =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003" Sdk="Microsoft.NET.Sdk">
              <PropertyGroup Condition=" '$(Configuration)' == 'Debug' ">
                <GodotProjectDir>..</GodotProjectDir>
                <UseWindowsForms>True</UseWindowsForms>
              </PropertyGroup>
            </Project>
            """;
        Assert.Equal(HostKind.WinForms, HostScan.Classify(csproj, "x"));
    }

    [Fact]
    public void Classify_IgnoresCommentsAndFallsBackOnBrokenXml()
    {
        const string commented =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- not a browser-wasm host, honestly -->
              <PropertyGroup><OutputType>Exe</OutputType><GodotProjectDir>..</GodotProjectDir></PropertyGroup>
            </Project>
            """;
        Assert.Equal(HostKind.Desktop, HostScan.Classify(commented, "x"));

        const string broken = "<Project><PropertyGroup><GodotProjectDir>..</GodotProjectDir><RuntimeIdentifier>browser-wasm";
        Assert.Equal(HostKind.Web, HostScan.Classify(broken, "x"));
        Assert.Equal(HostKind.Web, HostScan.ClassifyText(broken, "x"));
    }

    [Fact]
    public void GodotProjectFile_PreservesCrlfAndByteOrderMark()
    {
        using var tmp = new TempProjectDir();
        var path = Path.Combine(tmp.Dir, "project.godot");
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        File.WriteAllBytes(path, bom.Concat(Encoding.UTF8.GetBytes(
            "config_version=5\r\n\r\n[application]\r\n\r\nconfig/name=\"Game\"\r\n\r\n[dotnet]\r\n\r\nproject/assembly_name=\"Old\"\r\n")).ToArray());

        new GodotProjectFile(path).SetAssemblyName("New");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(bom, bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("project/assembly_name=\"New\"", text);
        Assert.DoesNotMatch("[^\r]\n", text);

        File.WriteAllText(path, "config_version=5\n\n[application]\n\nconfig/name=\"Game\"\n");
        new GodotProjectFile(path).AppendDotnetSection("Game");
        var appended = File.ReadAllText(path);
        Assert.DoesNotContain("\r", appended);
        Assert.EndsWith("[dotnet]\n\nproject/assembly_name=\"Game\"\n", appended);
        Assert.Equal("Game", new GodotProjectFile(path).Get("dotnet", "project/assembly_name"));
    }

    [Fact]
    public void GodotProjectFile_UnescapesQuotedValues_AndSetsArbitraryKeys()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("project.godot", "[application]\n\nconfig/name=\"A \\\"B\\\" C:\\\\D\"\n");
        var file = new GodotProjectFile(path);
        Assert.Equal("A \"B\" C:\\D", file.Get("application", "config/name"));

        file.Set("xr", "shaders/enabled.web", "true");
        Assert.Equal("true", new GodotProjectFile(path).Get("xr", "shaders/enabled.web"));
        Assert.Contains("\n[xr]\n\nshaders/enabled.web=\"true\"\n", File.ReadAllText(path));

        file.Set("application", "config/name", "Plain");
        Assert.Equal("Plain", new GodotProjectFile(path).Get("application", "config/name"));
    }

    [Fact]
    public void New_ThenAddWithTheSameFlags_HasNothingToDo()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");

        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--desktop", "--tests", "--no-restore").ExitCode);
        // The default set minus web is exactly what exists; a host flag would ask for a second host of that kind.
        var again = CliConsole.Run("add", dir, "--no-web", "--no-restore");
        Assert.Equal(0, again.ExitCode);
        Assert.Contains("Nothing to do", again.Stdout);

        var twice = CliConsole.Run("new", "Game", dir, "--desktop", "--no-restore");
        Assert.Equal(ExitCodes.Error, twice.ExitCode);
        Assert.Contains("already holds a Godot project", twice.Stderr);
    }

    [Fact]
    public void WebXrHost_EnablesTheXrShadersSetting_Unquoted()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--webxr", "--no-restore").ExitCode);
        // The template ships with the checkout's newline flavour; the appended section follows it.
        var godot = File.ReadAllText(Path.Combine(dir, "project.godot")).ReplaceLineEndings("\n");
        Assert.Contains("\n[xr]\n\nshaders/enabled.web=true\n", godot);
        Assert.DoesNotContain("shaders/enabled.web=\"true\"", godot);
        Assert.Equal("true", new GodotProjectFile(Path.Combine(dir, "project.godot")).Get("xr", "shaders/enabled.web"));
    }

    [Fact]
    public void New_IntoANonEmptyDirectory_Warns()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("notes.txt", "keep me");

        var run = CliConsole.Run("new", "Game", tmp.Dir, "--desktop", "--dry-run", "--no-restore");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning:", run.Stderr);
        Assert.Contains("is not empty", run.Stderr);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(tmp.Dir, "notes.txt")));
    }

    [Fact]
    public void PlanSummary_CountsByKind()
    {
        ActionReport A(ActionKind kind) => new("x", kind, ActionStatus.Planned);
        var summary = Out.PlanSummary([A(ActionKind.CreateDir), A(ActionKind.CreateFile), A(ActionKind.CreateFile),
            A(ActionKind.Patch), A(ActionKind.Solution), A(ActionKind.Restore)]);
        Assert.Equal("3 files, 1 patch, 1 solution step, 1 restore", summary);
    }

    [Fact]
    public void ProjectDir_IsNormalizedWithoutTrailingSeparator()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");
        var project = ScaffoldCommand.Open(new ScaffoldOptions { ProjectPath = tmp.Dir + Path.DirectorySeparatorChar });
        Assert.Equal(tmp.Dir, project.Dir);
    }
}
