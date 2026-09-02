using twodog.cli;

namespace twodog.tests.ToolTests;

// The pure halves of the `2dog version` nuget.org check: picking the latest
// stable out of a flat-container version list, and turning current-vs-latest
// into the up-to-date mark. The HTTP path is best-effort by design and not tested.
public class NuGetLatestTests
{
    [Fact]
    public void LatestStable_PicksHighestStable_NumericallyNotLexically()
    {
        Assert.Equal("4.10.0", NuGetLatest.LatestStable(["4.7.1.65", "4.10.0", "4.9.2"]));
    }

    [Fact]
    public void LatestStable_IgnoresPrereleases()
    {
        Assert.Equal("4.7.1.65", NuGetLatest.LatestStable(["4.7.1.65", "4.8.0-rc.1", "5.0.0-dev.2"]));
    }

    [Fact]
    public void LatestStable_NullWhenOnlyPrereleasesOrUnparseable()
    {
        Assert.Null(NuGetLatest.LatestStable(["4.8.0-rc.1"]));
        Assert.Null(NuGetLatest.LatestStable([]));
        Assert.Null(NuGetLatest.LatestStable(["not-a-version"]));
    }

    [Theory]
    [InlineData("4.7.1.65", "4.7.1.65", VersionMark.UpToDate)] // exactly the newest published
    [InlineData("4.7.1.66", "4.7.1.65", VersionMark.UpToDate)] // ahead of nuget.org: nothing newer to fetch
    [InlineData("4.7.1.35", "4.7.1.65", VersionMark.Outdated)]
    [InlineData("4.7.1", "4.9.1", VersionMark.Outdated)]
    [InlineData("4.7.1.65", null, null)] // lookup failed
    public void Mark_ComparesCurrentAgainstLatestStable(string current, string? latest, VersionMark? expected)
    {
        Assert.Equal(expected, NuGetLatest.Mark(current, latest));
    }
}
