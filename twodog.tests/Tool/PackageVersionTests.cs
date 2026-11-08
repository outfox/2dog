using twodog.cli;

namespace twodog.tests.ToolTests;

// 2dog versions are <Godot numbers>.<TwoDogRevision>[-<branch label>], with one revision sequence across every line, so
// a prerelease line (4.8.0.154-beta) and the release line (4.7.2) publish side by side and NuGet orders both correctly.
[Trait("Precision", "Independent")]
public class PackageVersionTests
{
    [Fact]
    public void ReleasesAcrossTwoLines_OrderAsNuGetOrdersThem()
    {
        string[] published = ["4.7.2.153", "4.8.0.154-beta", "4.7.2.155", "4.8.0.156-rc", "4.8.0.160"];
        var ordered = published.Select(v => PackageVersion.Parse(v)!).Order().Select(v => v.Text).ToArray();
        Assert.Equal(["4.7.2.153", "4.7.2.155", "4.8.0.154-beta", "4.8.0.156-rc", "4.8.0.160"], ordered);
    }

    [Theory]
    [InlineData("4.7.2.152", "4.7.2", false)]
    [InlineData("4.8.0.154-beta", "4.8.0", true)] // a release from the 4.8.0-beta branch
    [InlineData("4.8.0.156-RC", "4.8.0", true)]
    [InlineData("4.8.0.157-dev", "4.8.0", true)]
    [InlineData("4.8.0-beta.2", "4.8.0", true)] // Godot.NET.Sdk's own form
    [InlineData("4.8.0-dev.7", "4.8.0", true)]
    [InlineData("4.8.0.154-beta.1", "4.8.0", false)] // 2dog line labels carry no numbers
    [InlineData("4.7.2.139-sora", "4.7.2", false)] // a fork's suffix is not an official label
    [InlineData("4.7.2.139-sora.2", "4.7.2", false)]
    public void GodotVersion_IsTheGodotReleaseNumbers(string version, string godot, bool official)
    {
        var parsed = PackageVersion.Parse(version)!;
        Assert.Equal(godot, parsed.GodotVersion);
        Assert.Equal(official, parsed.IsOfficialPrerelease);
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
        string[] versions = ["4.7.2.155", "4.8.0.154-beta", "4.8.0.156-rc", "4.9.0.170-beta"];
        Assert.Equal("4.8.0.156-rc", NuGetLatest.Latest(versions, "4.8.0.154-beta"));
        Assert.Equal("4.8.0.160", NuGetLatest.Latest([.. versions, "4.8.0.160"], "4.8.0.154-beta"));
        // `dnx 2dog` users on a release only hear about releases.
        Assert.Equal("4.7.2.155", NuGetLatest.Latest(versions, "4.7.2.153"));
    }
}
