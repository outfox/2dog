using System.Text.RegularExpressions;
using twodog.cli;

namespace twodog.tests.ToolTests;

// `2dog update`: literal versions move into the props block, the block follows the tool, nothing ever downgrades.
public class UpdateTests
{
    /// <summary>A project as an older tool left it: literal versions in every csproj and no props block.</summary>
    private static string AgedProject(TempProjectDir tmp)
    {
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--desktop", "--web", "--tests", "--no-restore").ExitCode);
        File.Delete(Path.Combine(dir, "Directory.Build.props"));
        foreach (var csproj in Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(csproj)
                .Replace("Version=\"$(TwoDogVersion)\"", "Version=\"4.7.1.10\"")
                .Replace("Version=\"[$(TwoDogNativesVersion)]\"", "Version=\"[4.7.1.1]\"")
                .Replace("Version=\"$(TwoDogGodotVersion)\"", "Version=\"4.7.1\"")
                .Replace($"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}", "Godot.NET.Sdk/4.7.1");
            File.WriteAllText(csproj, text);
        }

        return dir;
    }

    private static T WithRunner<T>(IProcessRunner runner, Func<T> action)
    {
        var previous = UpdateCommand.Runner;
        UpdateCommand.Runner = runner;
        try { return action(); }
        finally { UpdateCommand.Runner = previous; }
    }

    [Fact]
    public void Update_MigratesLiterals_AndSetsTheBlock()
    {
        using var tmp = new TempProjectDir();
        var dir = AgedProject(tmp);

        var dry = CliConsole.Run("update", dir, "--dry-run", "--no-restore", "--allow-dirty");
        Assert.Equal(0, dry.ExitCode);
        Assert.Contains("would: switch Game.2dog/Game.2dog.csproj to the shared version properties", dry.Stdout);
        Assert.Contains("would: create Directory.Build.props", dry.Stdout);
        Assert.Contains($"would: set Directory.Build.props: TwoDogVersion 4.7.1.10 -> {ToolVersions.TwoDogVersion}", dry.Stdout);
        Assert.Contains($"would: set Game.csproj Sdk to Godot.NET.Sdk/{ToolVersions.GodotSdkVersion} (was 4.7.1)", dry.Stdout);
        Assert.False(File.Exists(Path.Combine(dir, "Directory.Build.props")));

        var run = CliConsole.Run("update", dir, "--no-restore", "--allow-dirty");
        Assert.Equal(0, run.ExitCode);
        var props = PropsPatcher.Read(Path.Combine(dir, "Directory.Build.props"));
        Assert.Equal(ToolVersions.TwoDogVersion, props["TwoDogVersion"]);
        Assert.Equal(ToolVersions.NativesVersion, props["TwoDogNativesVersion"]);
        Assert.Equal(ToolVersions.GodotSdkVersion, props["TwoDogGodotVersion"]);
        var web = File.ReadAllText(Path.Combine(dir, "Game.web", "Game.web.csproj"));
        Assert.Contains("Version=\"$(TwoDogVersion)\"", web);
        Assert.Contains("Version=\"[$(TwoDogNativesVersion)]\"", web);
        Assert.DoesNotMatch(@"Version=""\[?4\.7\.1", web);
        Assert.Contains("Version=\"$(TwoDogGodotVersion)\"", File.ReadAllText(Path.Combine(dir, "Game.tests", "Game.tests.csproj")));
        Assert.Contains($"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}", File.ReadAllText(Path.Combine(dir, "Game.csproj")));

        var again = CliConsole.Run("update", dir, "--no-restore", "--allow-dirty");
        Assert.Equal(0, again.ExitCode);
        Assert.Contains("Nothing to do", again.Stdout);
    }

    [Fact]
    public void Update_RefusesADirtyTree_UnlessAllowed()
    {
        using var tmp = new TempProjectDir();
        var dir = AgedProject(tmp);
        var runner = new FakeProcessRunner(r => r.FileName == "git"
            ? FakeProcessRunner.Result(r, 0, " M Game.csproj")
            : FakeProcessRunner.Result(r, 0));

        var refused = WithRunner(runner, () => CliConsole.Run("update", dir, "--no-restore"));
        Assert.Equal(ExitCodes.Error, refused.ExitCode);
        Assert.Contains("uncommitted changes", refused.Stderr);
        Assert.Contains("--allow-dirty", refused.Stderr);
        Assert.False(File.Exists(Path.Combine(dir, "Directory.Build.props")));

        var allowed = WithRunner(runner, () => CliConsole.Run("update", dir, "--allow-dirty"));
        Assert.Equal(0, allowed.ExitCode);
        Assert.True(File.Exists(Path.Combine(dir, "Directory.Build.props")));
        Assert.Contains(runner.Requests, r => r.Args.Contains("restore"));
    }

    [Fact]
    public void Update_TreatsMissingGit_AsAClean()
    {
        using var tmp = new TempProjectDir();
        var dir = AgedProject(tmp);
        var runner = new FakeProcessRunner(r => r.FileName == "git"
            ? throw new ToolException("could not start 'git'")
            : FakeProcessRunner.Result(r, 0));

        var run = WithRunner(runner, () => CliConsole.Run("update", dir, "--no-restore"));
        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Update_NeverDowngrades()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--desktop", "--no-restore").ExitCode);
        var props = Path.Combine(dir, "Directory.Build.props");
        File.WriteAllText(props, File.ReadAllText(props).Replace($"<TwoDogVersion>{ToolVersions.TwoDogVersion}<", "<TwoDogVersion>99.0.0.1<"));

        var run = CliConsole.Run("update", dir, "--no-restore", "--allow-dirty");
        Assert.Equal(ExitCodes.Error, run.ExitCode);
        Assert.Contains("newer than this tool", run.Stderr);
        Assert.Contains("dnx 2dog update", run.Stderr);
    }

    [Fact]
    public void Update_AcrossGodotLines_WarnsAboutTheEditor()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--desktop", "--no-restore").ExitCode);
        var game = Path.Combine(dir, "Game.csproj");
        File.WriteAllText(game, File.ReadAllText(game).Replace($"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}", "Godot.NET.Sdk/4.6.0"));

        var run = CliConsole.Run("update", dir, "--no-restore", "--allow-dirty", "--dry-run");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: this moves the project from Godot 4.6 to", run.Stderr);
        Assert.Contains("2dog doctor --build", run.Stderr);
    }

    [Fact]
    public void Update_RefreshesADriftedWebBootstrap()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--desktop", "--web", "--no-restore").ExitCode);
        var boot = Path.Combine(dir, "Game.web", "TwoDogWebBoot.cs");
        File.WriteAllText(boot, "// stale\n");

        var run = CliConsole.Run("update", dir, "--no-restore", "--allow-dirty");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("refresh Game.web/TwoDogWebBoot.cs", run.Stdout);
        Assert.Equal(TemplateAssets.WebBootSource(), File.ReadAllText(boot));
    }

    [Fact]
    public void UpdateTo_ExplainsHowToPickAVersion()
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(["update", "--to", "1.2.3"]));
        Assert.Contains("dnx 2dog@<version> update", ex.Message);
        Assert.Equal(Verb.Update, ex.Verb);
    }

    [Fact]
    public void VersionRewriter_MapsPackagesAndReadsTheSdk()
    {
        const string csproj =
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="2dog.engine" Version="4.7.1.10"/>
                <PackageReference Include="2dog.browser-wasm" Version="[4.7.1.1]"/>
                <PackageReference Include="Avalonia.Controls.DataGrid" Version="12.0.0"/>
                <PackageReference Include="xunit.v3" Version="3.*"/>
              </ItemGroup>
            </Project>
            """;
        var literals = VersionRewriter.Literals(csproj);
        Assert.Equal(["2dog.engine", "2dog.browser-wasm"], literals.Select(l => l.Id));
        Assert.True(literals[1].IsPinned);
        Assert.Equal(new Version(4, 7, 1, 1), literals[1].Parsed);
        Assert.Equal("[$(TwoDogNativesVersion)]", VersionRewriter.Reference("2dog.browser-wasm"));
        Assert.Null(VersionRewriter.PropertyFor("Avalonia.Controls.DataGrid"));

        Assert.Equal("4.7.1", VersionRewriter.GodotSdkVersion("<Project Sdk=\"Godot.NET.Sdk/4.7.1\">"));
        Assert.Null(VersionRewriter.SetGodotSdkVersion("<Project Sdk=\"Godot.NET.Sdk/4.7.2\">", "4.7.2"));
        Assert.Equal("<Project Sdk=\"Godot.NET.Sdk/4.7.2\">", VersionRewriter.SetGodotSdkVersion("<Project Sdk=\"Godot.NET.Sdk/4.7.1\">", "4.7.2"));
    }

    [Fact]
    public void Migrate_KeepsEverythingElseByteIdentical()
    {
        using var tmp = new TempProjectDir();
        var csproj = tmp.Write("x.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <!-- keep -->\n  <ItemGroup>\n    <PackageReference Include=\"2dog.engine\" Version=\"4.7.1.10\" />\n" +
            "    <PackageReference Include=\"Serilog\"   Version=\"4.0.0\"/>\n  </ItemGroup>\n</Project>\n");

        var (text, changes) = VersionRewriter.Migrate(csproj);
        Assert.Equal(["2dog.engine 4.7.1.10 -> $(TwoDogVersion)"], changes);
        Assert.Equal(File.ReadAllText(csproj).Replace("Version=\"4.7.1.10\"", "Version=\"$(TwoDogVersion)\""), text);
        Assert.DoesNotContain("\r", text);
        Assert.Matches(new Regex("Serilog\"   Version"), text!);
    }
}
