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

    /// <summary>UpToDate when current is the newest stable on nuget.org (or ahead of it), null if unknown.</summary>
    public static VersionMark? Mark(string current, string? latestStable)
    {
        if (latestStable == null) return null;
        if (Version.TryParse(current, out var c) && Version.TryParse(latestStable, out var l))
            return c >= l ? VersionMark.UpToDate : VersionMark.Outdated;
        return latestStable == current ? VersionMark.UpToDate : null;
    }

    /// <summary>Latest stable version per package id; null entries where the lookup failed.</summary>
    public static IReadOnlyDictionary<string, string?> Query(IEnumerable<string> packageIds)
    {
        using var http = new HttpClient { Timeout = Timeout };
        var lookups = packageIds.Distinct().ToDictionary(id => id, id => QueryOne(http, id));
        Task.WaitAll(lookups.Values.ToArray<Task>(), Timeout);
        return lookups.ToDictionary(l => l.Key, l => l.Value.IsCompletedSuccessfully ? l.Value.Result : null);
    }

    private static async Task<string?> QueryOne(HttpClient http, string packageId)
    {
        try
        {
            var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            return LatestStable(doc.RootElement.GetProperty("versions").EnumerateArray()
                .Select(v => v.GetString()).OfType<string>().ToList());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The highest non-prerelease version, or null when there is none.</summary>
    internal static string? LatestStable(IEnumerable<string> versions) =>
        versions.Where(v => !v.Contains('-'))
            .Select(v => (Text: v, Parsed: Version.TryParse(v, out var p) ? p : null))
            .Where(v => v.Parsed != null)
            .OrderByDescending(v => v.Parsed)
            .Select(v => v.Text)
            .FirstOrDefault();
}
