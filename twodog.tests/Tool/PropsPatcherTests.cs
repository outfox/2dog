using twodog.cli;

namespace twodog.tests.ToolTests;

// The root Directory.Build.props carries the package versions every host references: the template ships it,
// `add` creates or appends it, and no host csproj carries a literal version any more.
public class PropsPatcherTests
{
    [Fact]
    public void TemplateHosts_ReferenceVersionsThroughProperties()
    {
        foreach (var kind in Hosts.All)
        {
            var folder = $"Game.{Hosts.Suffix(kind)}";
            foreach (var file in TemplateAssets.HostFiles(kind, "Game", folder).Where(f => f.RelativePath.EndsWith(".csproj")))
            {
                Assert.DoesNotContain("_PKG_VERSION", file.Text);
                Assert.DoesNotContain("GODOT_SDK_VERSION", file.Text);
                Assert.Contains("$(TwoDog", file.Text);
            }
        }

        var props = TemplateAssets.RootBuildProps();
        Assert.Contains($"<TwoDogVersion>{ToolVersions.TwoDogVersion}</TwoDogVersion>", props);
        Assert.Contains($"<TwoDogNativesVersion>{ToolVersions.NativesVersion}</TwoDogNativesVersion>", props);
        Assert.Contains($"<TwoDogGodotVersion>{ToolVersions.GodotSdkVersion}</TwoDogGodotVersion>", props);
        Assert.Contains("GetPathOfFileAbove('Directory.Build.props'", props);
    }

    [Fact]
    public void New_CreatesTheProps_AndHostsResolveThroughThem()
    {
        using var tmp = new TempProjectDir();
        var dir = Path.Combine(tmp.Dir, "Game");
        Assert.Equal(0, CliConsole.Run("new", "Game", dir, "--desktop", "--web", "--tests", "--no-restore").ExitCode);

        var props = File.ReadAllText(Path.Combine(dir, "Directory.Build.props"));
        Assert.Contains("Label=\"2dog\"", props);
        Assert.Contains($"<TwoDogVersion>{ToolVersions.TwoDogVersion}</TwoDogVersion>", props);
        Assert.Contains("Version=\"$(TwoDogVersion)\"", File.ReadAllText(Path.Combine(dir, "Game.2dog", "Game.2dog.csproj")));
        Assert.Contains("Version=\"[$(TwoDogNativesVersion)]\"", File.ReadAllText(Path.Combine(dir, "Game.web", "Game.web.csproj")));
        Assert.Contains($"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}", File.ReadAllText(Path.Combine(dir, "Game.csproj")));
    }

    [Fact]
    public void Add_AppendsTheBlockToAUserOwnedProps_Once()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");
        var props = tmp.Write("Directory.Build.props",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Project>\n  <PropertyGroup>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n");

        var first = CliConsole.Run("add", tmp.Dir, "--desktop", "--no-restore");
        Assert.Equal(0, first.ExitCode);
        Assert.Contains("append the 2dog version block to your Directory.Build.props", first.Stdout);

        var text = File.ReadAllText(props);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
        Assert.Contains("<Nullable>enable</Nullable>", text);
        Assert.Contains("<PropertyGroup Label=\"2dog\">", text);
        Assert.Equal(ToolVersions.TwoDogVersion, PropsPatcher.Read(props)["TwoDogVersion"]);

        var second = CliConsole.Run("add", tmp.Dir, "--no-web", "--no-restore");
        Assert.Equal(0, second.ExitCode);
        Assert.DoesNotContain("Directory.Build.props", second.Stdout);
        Assert.Equal(text, File.ReadAllText(props));
    }

    [Fact]
    public void SetValues_RewritesOnlyTheBlock()
    {
        using var tmp = new TempProjectDir();
        var props = tmp.Write("Directory.Build.props",
            "<Project>\n  <!-- mine -->\n  <PropertyGroup Label=\"2dog\">\n    <TwoDogVersion>4.7.1.10</TwoDogVersion>\n" +
            "    <TwoDogNativesVersion>4.7.1.1</TwoDogNativesVersion>\n  </PropertyGroup>\n</Project>\n");

        Assert.Null(PropsPatcher.SetValues(props, [("TwoDogVersion", "4.7.1.10")]));

        var updated = PropsPatcher.SetValues(props, [("TwoDogVersion", "4.7.2.5"), ("TwoDogGodotVersion", "4.7.2")]);
        Assert.NotNull(updated);
        Assert.Contains("<!-- mine -->", updated);
        Assert.Contains("<TwoDogVersion>4.7.2.5</TwoDogVersion>", updated);
        Assert.Contains("<TwoDogNativesVersion>4.7.1.1</TwoDogNativesVersion>", updated);
        Assert.Contains("    <TwoDogGodotVersion>4.7.2</TwoDogGodotVersion>\n  </PropertyGroup>", updated);
    }
}
