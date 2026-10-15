using twodog.cli;

namespace twodog.tests.ToolTests;

public class MauiHostTests
{
    [Fact]
    public void NewMauiHost_UsesCustomFolder_AndLeavesOrdinaryBuildsAlone()
    {
        using var tmp = new TempProjectDir();
        var result = CliConsole.Run("new", "Game", tmp.Dir, "--maui", "Game.ui", "--no-restore");
        Assert.Equal(0, result.ExitCode);
        var project = File.ReadAllText(Path.Combine(tmp.Dir, "Game.ui", "Game.ui.csproj"));
        Assert.Contains("<UseMaui>true</UseMaui>", project);
        Assert.Contains("2dog.maui", project);
        Assert.DoesNotContain("-ios", project);
        Assert.DoesNotContain("-maccatalyst", project);
        Assert.Contains("Game.ui/**", File.ReadAllText(Path.Combine(tmp.Dir, "Game.csproj")));
        Assert.Contains("<TwoDogMauiVersion>", File.ReadAllText(Path.Combine(tmp.Dir, "Directory.Build.props")));
        Assert.Contains("10.0.100", File.ReadAllText(Path.Combine(tmp.Dir, "global.json")));
        Assert.True(File.Exists(Path.Combine(tmp.Dir, "Game.ui", "global.json")));
        Assert.True(SolutionOps.IsExcludedFromSolutionBuild(Path.Combine(tmp.Dir, "Game.slnx"), "Game.ui/Game.ui.csproj"));
        Assert.Equal(HostKind.Maui, HostScan.Find(tmp.Dir).Single().Kind);
    }

    [Theory]
    [InlineData("<Project><PackageReference Include='2dog.maui'/><UseWinUI>true</UseWinUI></Project>")]
    [InlineData("<Project><GodotProjectDir>..</GodotProjectDir><UseMaui>true</UseMaui><TargetFrameworks>net10.0-android</TargetFrameworks></Project>")]
    [InlineData("<Project><PackageReference Include='2dog.maui'>")]
    public void Detection_PrefersMauiOverUnderlyingPlatforms(string project) =>
        Assert.Equal(HostKind.Maui, HostScan.Classify(project, "custom.mobile"));

    [Fact]
    public void MauiWorkloads_DoNotChangeDesktopRequirements()
    {
        Assert.Empty(Workload.For([HostKind.Desktop]));
        var required = Workload.For([HostKind.Maui]).ToList();
        Assert.Contains(Workload.MauiAndroid, required);
        Assert.Equal(OperatingSystem.IsWindows(), required.Contains(Workload.MauiWindows));
        Assert.False(new ScaffoldOptions { InstallWasmTools = true }.InstallRequested(Workload.MauiAndroid));
        Assert.Equal("TwoDogVersion", VersionRewriter.PropertyFor("2dog.maui"));
        Assert.Equal("TwoDogMauiVersion", VersionRewriter.PropertyFor("Microsoft.Maui.Controls"));
    }
}
