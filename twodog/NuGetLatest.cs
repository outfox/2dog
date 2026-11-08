using System.Text.Json;

namespace twodog.cli;

/// <summary>How a version compares to the newest stable on nuget.org.</summary>
public enum VersionMark
{
    UpToDate,
    Outdated,
}

/// <summary>
/// Best-effort nuget.org lookup behind the up-to-date marks of `2dog version`; any failure leaves rows unmarked.
/// </summary>
internal static class NuGetLatest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5);

    /// <summary>UpToDate when current is the newest on nuget.org (or ahead of it), null if unknown.</summary>
    public static VersionMark? Mark(string current, string? latest)
    {
        if (latest == null) return null;
        if (PackageVersion.Parse(current) is { } c && PackageVersion.Parse(latest) is { } l)
            return c >= l ? VersionMark.UpToDate : VersionMark.Outdated;
        return latest == current ? VersionMark.UpToDate : null;
    }

    /// <summary>Per package id, the newest version a user of the given current one should hear about (see
    /// <see cref="Latest"/>); null entries where the lookup failed.</summary>
    public static IReadOnlyDictionary<string, string?> Query(IEnumerable<(string Id, string Current)> packages)
    {
        using var http = new HttpClient { Timeout = Timeout };
        var lookups = packages.DistinctBy(p => p.Id).ToDictionary(p => p.Id, p => QueryOne(http, p.Id, p.Current));
        Task.WaitAll(lookups.Values.ToArray<Task>(), Timeout);
        return lookups.ToDictionary(l => l.Key, l => l.Value.IsCompletedSuccessfully ? l.Value.Result : null);
    }

    private static async Task<string?> QueryOne(HttpClient http, string packageId, string current)
    {
        try
        {
            var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            return Latest(doc.RootElement.GetProperty("versions").EnumerateArray()
                .Select(v => v.GetString()).OfType<string>().ToList(), current);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The highest non-prerelease version, or null when there is none.</summary>
    internal static string? LatestStable(IEnumerable<string> versions) => Latest(versions, null);

    /// <summary>
    /// The highest release, or for a prerelease <paramref name="current"/> the highest version of its Godot release,
    /// prereleases included: 4.8.0.156-beta2 hears about 4.8.0.158-rc1 and 4.8.0.160, never 4.9 previews.
    /// </summary>
    internal static string? Latest(IEnumerable<string> versions, string? current)
    {
        var mine = PackageVersion.Parse(current);
        return versions.Select(PackageVersion.Parse).OfType<PackageVersion>()
            .Where(v => !v.IsPrerelease || mine is { IsPrerelease: true } && (v.Major, v.Minor, v.Build) == (mine.Major, mine.Minor, mine.Build))
            .Max()?.Text;
    }
}
