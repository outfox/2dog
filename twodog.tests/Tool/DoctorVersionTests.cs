using twodog.cli;

namespace twodog.tests.ToolTests;

[Trait("Precision", "Independent")]
public class DoctorVersionTests
{
    private static DoctorContext Context(string engine, string natives, string? reference = null, string godot = "4.7.2") => new()
    {
        Project = new ProjectModel
        {
            Dir = Path.GetTempPath(),
            PropsValues = new(StringComparer.OrdinalIgnoreCase)
            {
                ["twodogversion"] = engine,
                ["TwoDogNativesVersion"] = natives,
                ["TwoDogGodotVersion"] = godot
            },
            GameCsprojText = $"""
                <Project Sdk="Godot.NET.Sdk/{godot}">
                  <ItemGroup><PackageReference Include="2dog.godotsharp" Version="[{reference ?? engine}]" /></ItemGroup>
                </Project>
                """
        },
        Env = new FakeEnvironment(),
        Runner = new FakeProcessRunner(r => FakeProcessRunner.Result(r, 0)),
        Options = new DoctorOptions { Offline = true }
    };

    [Theory]
    [InlineData("4.7.2.139-sora", "4.7.2.55")]
    [InlineData("4.7.2.139", "4.7.2.55-sora")]
    [InlineData("4.7.2.139-sora.2", "4.7.2.55-sora.2")]
    public void CustomToolVersions_WarnInsteadOfThrowingOrSuggestingPublicUpgrades(string tool, string toolNatives)
    {
        var findings = VersionChecks.Run(Context("4.7.2.139", "4.7.2.55"), tool, toolNatives).ToList();

        var warning = Assert.Single(findings, f => f.Id == "ver.custom-versions");
        Assert.Equal(Severity.Warn, warning.Severity);
        Assert.Contains("best effort", warning.Detail);
        Assert.DoesNotContain(findings, f => f.Id is "ver.twodog-outdated" or "ver.twodog-newer" or "ver.tool-latest");
        Assert.DoesNotContain(findings, f => f.Remedy?.Contains("2dog update") == true);
    }

    [Theory]
    [InlineData("4.7.2.139-sora", false)]
    [InlineData("4.7.2.139", true)]
    [InlineData("4.7.2.139-other", true)]
    [InlineData("4.7.2.139-sora.2", true)]
    public void CustomProjectVersions_CheckTheFullLabel(string reference, bool mixed)
    {
        var findings = VersionChecks.Run(Context("4.7.2.139-sora", "4.7.2.55-sora", reference)).ToList();

        Assert.Contains(findings, f => f.Id == "ver.custom-versions" && f.Severity == Severity.Warn);
        Assert.DoesNotContain(findings, f => f.Id == "ver.props-invalid");
        Assert.Equal(mixed, findings.Any(f => f.Id == "ver.twodog-consistent" && f.Severity == Severity.Fail));
        Assert.DoesNotContain(findings, f => f.Remedy?.Contains("2dog update") == true);
    }

    [Theory]
    [InlineData("4.7.2.55-sora", false)]
    [InlineData("4.6.2.55-sora", true)]
    public void CustomNatives_StillCheckTheNumericGodotLine(string natives, bool mismatch)
    {
        var findings = VersionChecks.Run(Context("4.7.2.139-sora", natives)).ToList();

        var native = Assert.Single(findings, f => f.Id == "ver.natives");
        Assert.Equal(mismatch ? Severity.Fail : Severity.Pass, native.Severity);
        Assert.Contains(natives, native.Title);
    }

    [Theory]
    [InlineData("4.8.0.152", false)]
    [InlineData("4.7.2.62", true)] // natives from another Godot release than the engine
    public void GodotPrereleases_AreOfficial_AndMatchNativesByGodotNumbers(string natives, bool mismatch)
    {
        var findings = VersionChecks.Run(Context("4.8.0.156-beta2", natives, godot: "4.8.0-beta.2"),
            "4.8.0.156-beta2", "4.8.0.152").ToList();

        Assert.DoesNotContain(findings, f => f.Id == "ver.custom-versions");
        Assert.Equal(mismatch ? Severity.Fail : Severity.Pass, Assert.Single(findings, f => f.Id == "ver.natives").Severity);
        Assert.Contains(findings, f => f.Id == "ver.twodog-outdated" && f.Severity == Severity.Pass);
    }

    [Fact]
    public void PrereleaseProjects_HearAboutNewerPrereleasesOfTheTool()
    {
        var findings = VersionChecks.Run(Context("4.8.0.156-beta2", "4.8.0.152", godot: "4.8.0-beta.2"),
            "4.8.0.158-rc1", "4.8.0.158").ToList();

        Assert.Contains(findings, f => f.Id == "ver.twodog-outdated" && f.Title.Contains("-> 4.8.0.158-rc1"));
    }

    [Fact]
    public void CustomNativeLiterals_StillRequireAnExactPin()
    {
        var ctx = Context("4.7.2.139-sora", "4.7.2.55-sora");
        ctx.Project.Hosts.Add(new HostModel
        {
            Kind = HostKind.Web, Folder = "Game.web", CsprojPath = "Game.web.csproj",
            Packages = [new PackageRef("2dog.browser-wasm", "4.7.2.55-sora")]
        });

        Assert.Contains(VersionChecks.Run(ctx), f => f.Id == "ver.natives" && f.Severity == Severity.Warn
                                                    && f.Title.Contains("not exact-pinned"));
    }
}
