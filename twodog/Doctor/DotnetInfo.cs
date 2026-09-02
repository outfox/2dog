using System.Text.Json;
using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>Pure parsers for the `dotnet` listings doctor relies on, and the global.json roll-forward rules.</summary>
internal static class DotnetInfo
{
    /// <summary>An installed SDK: the numeric part and the full string (previews carry a suffix).</summary>
    public sealed record Sdk(Version Version, string Raw)
    {
        public bool IsPreview => Raw.Contains('-');
        public int FeatureBand => Version.Build / 100 * 100;
    }

    private static readonly Regex SdkLine = new(@"^(?<raw>(?<ver>\d+\.\d+\.\d+)[^\s\[]*)\s+\[(?<dir>.+)\]\s*$", RegexOptions.Compiled);

    /// <summary>`dotnet --list-sdks` lines such as "10.0.303 [C:\Program Files\dotnet\sdk]", newest first.</summary>
    public static List<Sdk> ParseSdks(IEnumerable<string> lines) => lines
        .Select(l => SdkLine.Match(l.Trim()))
        .Where(m => m.Success)
        .Select(m => new Sdk(Version.Parse(m.Groups["ver"].Value), m.Groups["raw"].Value))
        .OrderByDescending(s => s.Version).ThenBy(s => s.IsPreview)
        .ToList();

    /// <summary>
    /// Installed workload ids from `dotnet workload list`: the first token of every table row after the dashed
    /// header line, up to the first blank line.
    /// </summary>
    public static List<string> ParseWorkloads(IEnumerable<string> lines)
    {
        var ids = new List<string>();
        var inTable = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (!inTable)
            {
                inTable = line.StartsWith("---", StringComparison.Ordinal);
                continue;
            }

            if (line.Trim().Length == 0) break;
            var id = line.Trim().Split(' ', 2)[0];
            if (Regex.IsMatch(id, "^[a-z][a-z0-9.-]*$")) ids.Add(id);
        }

        return ids;
    }

    /// <summary>The path after "global-packages: " in `dotnet nuget locals global-packages -l`.</summary>
    public static string? ParseGlobalPackages(IEnumerable<string> lines) => lines
        .Select(l => l.Trim())
        .Where(l => l.StartsWith("global-packages:", StringComparison.OrdinalIgnoreCase))
        .Select(l => l["global-packages:".Length..].Trim())
        .FirstOrDefault(p => p.Length > 0);

    /// <summary>The sdk.version and sdk.rollForward of a global.json; nulls when absent or unreadable.</summary>
    public static (Version? Version, string RollForward) ParseGlobalJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("sdk", out var sdk)) return (null, "latestPatch");
            var version = sdk.TryGetProperty("version", out var v) && Version.TryParse(v.GetString()?.Split('-')[0], out var parsed) ? parsed : null;
            var roll = sdk.TryGetProperty("rollForward", out var r) ? r.GetString() ?? "latestPatch" : "latestPatch";
            return (version, roll);
        }
        catch (JsonException)
        {
            return (null, "latestPatch");
        }
    }

    /// <summary>Whether an installed SDK satisfies the pin under its roll-forward policy (the common policies).</summary>
    public static bool Satisfies(Version pin, string rollForward, IEnumerable<Sdk> installed)
    {
        var band = pin.Build / 100 * 100;
        return installed.Any(s => rollForward.ToLowerInvariant() switch
        {
            "disable" => s.Version == pin,
            "patch" or "latestpatch" => s.Version >= pin && s.Version.Major == pin.Major && s.Version.Minor == pin.Minor && s.FeatureBand == band,
            "feature" or "latestfeature" => s.Version >= pin && s.Version.Major == pin.Major && s.Version.Minor == pin.Minor,
            "minor" or "latestminor" => s.Version >= pin && s.Version.Major == pin.Major,
            _ => s.Version >= pin,
        });
    }
}
