using twodog.cli;

namespace twodog.tests.ToolTests;

// 2dog versions are <GodotVersion>.<TwoDogRevision>, with one revision sequence across every Godot line, so a Godot
// prerelease line (4.8.0-beta.1) and the release line (4.7.2) publish side by side and NuGet orders both correctly.
[Trait("Precision", "Independent")]
public class PackageVersionTests
{
    [Fact]
    public void ReleasesAcrossTwoLines_OrderAsNuGetOrdersThem()
    {
        string[] published = ["4.7.2.153", "4.8.0-beta.1.154", "4.7.2.155", "4.8.0-rc.1.156", "4.8.0.160"];
        var ordered = published.Select(v => PackageVersion.Parse(v)!).Order().Select(v => v.Text).ToArray();
        Assert.Equal(["4.7.2.153", "4.7.2.155", "4.8.0-beta.1.154", "4.8.0-rc.1.156", "4.8.0.160"], ordered);
    }

    [Theory]
    [InlineData("4.7.2.152", "4.7.2", false)]
    [InlineData("4.8.0-beta.1.154", "4.8.0-beta.1", true)]
    [InlineData("4.8.0-rc.2.0", "4.8.0-rc.2", true)] // natives at NativesRevision 0
    [InlineData("4.8.0-beta.152", "4.8.0-beta", true)] // a development build between Godot betas
    [InlineData("4.7.2.139-sora", "4.7.2", false)] // a fork's suffix is not a Godot label
    [InlineData("4.7.2.139-sora.2", "4.7.2", false)]
    public void GodotVersion_IsTheVersionWithoutTheRevision(string version, string godot, bool official)
    {
        var parsed = PackageVersion.Parse(version)!;
        Assert.Equal(godot, parsed.GodotVersion);
        Assert.Equal(official, parsed.IsGodotPrerelease);
    }

    [Theory]
    [InlineData("4.7.2.0", "4.7.2")]
    [InlineData("4.8.0-Beta.1.0", "4.8.0-beta.1.0")] // NuGet keeps a label's zero, lower-cased
    [InlineData("[4.7.2.55]", "4.7.2.55")]
    public void Normalized_MatchesTheNuGetCacheFolder(string version, string folder) =>
        Assert.Equal(folder, PackageVersion.Parse(version)!.Normalized);

    [Theory]
    [InlineData("$(TwoDogVersion)")]
    [InlineData("10.0.*")]
    [InlineData("[4.7.1,5.0.0]")]
    [InlineData("4.8.0-")]
    public void Parse_RejectsWhatIsNotOneVersion(string text) => Assert.Null(PackageVersion.Parse(text));

    [Fact]
    public void Latest_PrereleaseStaysOnItsGodotVersion()
    {
        string[] versions = ["4.7.2.155", "4.8.0-beta.1.154", "4.8.0-rc.1.156", "4.9.0-beta.1.170"];
        Assert.Equal("4.8.0-rc.1.156", NuGetLatest.Latest(versions, "4.8.0-beta.1.154"));
        Assert.Equal("4.8.0.160", NuGetLatest.Latest([.. versions, "4.8.0.160"], "4.8.0-beta.1.154"));
        // `dnx 2dog` users on a release only hear about releases.
        Assert.Equal("4.7.2.155", NuGetLatest.Latest(versions, "4.7.2.153"));
    }
}
