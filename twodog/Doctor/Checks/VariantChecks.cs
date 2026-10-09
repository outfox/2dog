using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace twodog.cli;

/// <summary>Static variant checks: literals and simple Configuration conditions, without requiring SDK workloads.</summary>
internal static class VariantChecks
{
    private static readonly Regex ConfigurationCondition = new(
        @"^\s*['"" ]*\$\(Configuration\)['"" ]*\s*(?<op>==|!=)\s*['""](?<value>[^'""]+)['""]\s*$",
        RegexOptions.IgnoreCase);

    internal static IEnumerable<Finding> Check(DoctorContext ctx, HostModel host)
    {
        // Web targets validate the engine's generic variant as well as their own linking variant.
        if (host.IsWebLike && CheckProperty(ctx, host, "TwoDogVariant", supportsEditor: true, useDefault: false) is { } generic)
            yield return generic;
        if (CheckProperty(ctx, host, host.IsWebLike ? "TwoDogWebVariant" : "TwoDogVariant",
                supportsEditor: !host.IsWebLike && host.Kind != HostKind.Android, useDefault: true) is { } finding)
            yield return finding;
    }

    private static Finding? CheckProperty(DoctorContext ctx, HostModel host, string property, bool supportsEditor, bool useDefault)
    {
        var path = host.Kind == HostKind.Blazor ? host.ClientCsprojPath : host.CsprojPath;
        if (path is null) return null;
        var documents = new List<XDocument>();
        var directories = new Stack<string>();
        for (var dir = Path.GetDirectoryName(path); dir != null; dir = Path.GetDirectoryName(dir))
            directories.Push(dir);
        string? parentProps = null;
        foreach (var dir in directories)
        {
            var props = Path.Combine(dir, "Directory.Build.props");
            if (!File.Exists(props)) continue;
            var doc = MsBuildXml.Load(props);
            // A nested props file shadows its parent unless it explicitly chains imports.
            if (parentProps is null || !LayoutChecks.ImportsParent(props, parentProps)) documents.Clear();
            documents.Add(doc);
            parentProps = props;
        }
        documents.Add(MsBuildXml.Load(path));

        string? value = "";
        foreach (var element in documents.SelectMany(doc => doc.Descendants())
                     .Where(e => e.Name.LocalName.Equals(property, StringComparison.OrdinalIgnoreCase)
                         && e.Parent?.Name.LocalName == "PropertyGroup"))
        {
            var conditions = new[] { (string?)element.Parent!.Attribute("Condition"), (string?)element.Attribute("Condition") }
                .Select(condition => Applies(condition, ctx.Options.Configuration)).ToList();
            if (conditions.Contains(false)) continue;
            value = conditions.Contains(null) || element.Ancestors().Any(e => e.Name.LocalName is "Choose" or "Target")
                || element.Value.Contains("$(") || element.Value.Contains("@(") || element.Value.Contains("%(")
                ? null : element.Value.Trim();
        }

        var relative = Path.GetRelativePath(ctx.Project.Dir, path).Replace('\\', '/');
        if (value is null)
            return new Finding("host.variant", Category.Hosts, Severity.Info, $"{relative}: {property} needs MSBuild evaluation",
                "static checks cannot resolve this expression or condition; use --build to evaluate the host", Path: relative);
        if (value.Length == 0)
        {
            // Excluded hosts are not built with the solution's Editor configuration; only diagnose the default
            // when that host was actually selected. Explicit invalid values are reported even without --build.
            if (!useDefault || !ctx.BuildHosts.Contains(host)) return null;
            value = ctx.Options.Configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) ? "debug"
                : ctx.Options.Configuration.Equals("Editor", StringComparison.OrdinalIgnoreCase) ? "editor" : "release";
        }
        var normalized = value.ToLowerInvariant(); // MSBuild string comparisons are case-insensitive.
        if (normalized is "release" or "debug" || supportsEditor && normalized == "editor") return null;
        var allowed = supportsEditor ? "release, debug, editor" : "release, debug";
        return new Finding("host.variant", Category.Hosts, Severity.Fail, $"{relative} selects {property} '{value}'",
            $"allowed: {allowed}; {Hosts.Label(host.Kind)} uses the same restrictions at build time",
            $"set {property} to a supported variant, or build with Debug or Release", relative);
    }

    private static bool? Applies(string? condition, string configuration)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        var match = ConfigurationCondition.Match(condition);
        if (!match.Success) return null;
        var equals = configuration.Equals(match.Groups["value"].Value, StringComparison.OrdinalIgnoreCase);
        return match.Groups["op"].Value == "==" ? equals : !equals;
    }
}
