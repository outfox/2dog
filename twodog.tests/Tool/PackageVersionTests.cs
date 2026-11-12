using twodog.cli;

namespace twodog.tests.ToolTests;

// 2dog versions are <Godot numbers>.<TwoDogRevision>[-<Godot status or branch label>], with one revision sequence across
// every line, so a prerelease line (4.8.0.156-beta2) and the release line (4.7.2) publish side by side and NuGet orders
// both correctly.
[Trait("Precision", "Independent")]
public class PackageVersionTests
{
    [Fact]
    public void ReleasesAcrossTwoLines_OrderAsNuGetOrdersThem()
    {
        string[] published = ["4.7.2.153", "4.8.0.156-beta2", "4.7.2.155", "4.8.0.158-rc1", "4.8.0.160", "4.8.0.157-beta10"];
        var ordered = published.Select(v => PackageVersion.Parse(v)!).Order().Select(v => v.Text).ToArray();
        Assert.Equal(["4.7.2.153", "4.7.2.155", "4.8.0.156-beta2", "4.8.0.157-beta10", "4.8.0.158-rc1", "4.8.0.160"], ordered);
    }

    // CI builds carry a ci label (Directory.Build.props) that sorts them after the release they follow and before the
    // next: a labelled line keeps its revision, an unlabelled one takes the next.
    [Fact]
    public void CiBuilds_OrderBetweenReleases()
    {
        string[] versions = ["4.7.2.165", "4.7.2.165-ci.812", "4.7.2.164", "4.8.0.163-beta3.ci.812", "4.8.0.163-beta3", "4.8.0.164-beta3"];
        var ordered = versions.Select(v => PackageVersion.Parse(v)!).Order().Select(v => v.Text).ToArray();
        Assert.Equal(["4.7.2.164", "4.7.2.165-ci.812", "4.7.2.165", "4.8.0.163-beta3", "4.8.0.163-beta3.ci.812", "4.8.0.164-beta3"], ordered);
    }

    [Fact]
    public void CiBuilds_OrderLargeRunIdsNumerically()
    {
        string[] versions = ["4.8.0.163-beta3.ci.100000000000", "4.8.0.163-beta3.ci.99999999999", "4.8.0.163-beta3.ci.9999999999"];
        Assert.Equal(versions.Reverse(), versions.Select(v => PackageVersion.Parse(v)!).Order().Select(v => v.Text));
    }

    [Theory]
    [InlineData("4.7.2.152", "4.7.2", false)]
    [InlineData("4.8.0.156-beta2", "4.8.0-beta.2", true)] // Godot's status names the Godot prerelease
    [InlineData("4.8.0.158-RC1", "4.8.0-rc.1", true)]
    [InlineData("4.8.0.157-dev7", "4.8.0-dev.7", true)]
    [InlineData("4.8.0.155-beta", "4.8.0", true)] // the first 4.8 release, labeled by its branch
    [InlineData("4.7.2.170-rc", "4.7.2", true)] // a 2dog prerelease of a stable Godot
    [InlineData("4.8.0-beta.2", "4.8.0", true)] // Godot.NET.Sdk's own form
    [InlineData("4.8.0.154-beta.1", "4.8.0", false)] // 2dog labels carry no dot
    [InlineData("4.7.2.139-sora", "4.7.2", false)] // a fork's suffix is not an official label
    [InlineData("4.7.2.139-sora.2", "4.7.2", false)]
    [InlineData("4.8.0.163-beta3.ci.812", "4.8.0-beta.3", true)] // a CI build means what the release it precedes means
    [InlineData("4.7.2.165-ci.812", "4.7.2", true)]
    [InlineData("4.7.2.139-sora.ci.3", "4.7.2", false)]
    [InlineData("4.8.0.163-beta3.ci.43226235920", "4.8.0-beta.3", true)]
    public void GodotVersion_FollowsGodotsStatus(string version, string godot, bool official)
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
        string[] versions = ["4.7.2.155", "4.8.0.156-beta2", "4.8.0.158-rc1", "4.9.0.170-beta1"];
        Assert.Equal("4.8.0.158-rc1", NuGetLatest.Latest(versions, "4.8.0.156-beta2"));
        Assert.Equal("4.8.0.160", NuGetLatest.Latest([.. versions, "4.8.0.160"], "4.8.0.156-beta2"));
        // `dnx 2dog` users on a release only hear about releases.
        Assert.Equal("4.7.2.155", NuGetLatest.Latest(versions, "4.7.2.153"));
    }
}
