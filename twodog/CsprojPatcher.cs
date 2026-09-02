using System.Xml.Linq;

namespace twodog.cli;

/// <summary>
/// Minimal in-place patching of an existing Godot project csproj: appends one clearly-marked PropertyGroup with the
/// 2dog properties not already present. Existing target frameworks are upgraded in place.
/// </summary>
internal static class CsprojPatcher
{
    private const string TargetFramework = "net10.0";

    public sealed record Result(string? NewContent, List<string> Added, List<string> Warnings);

    public static Result Patch(string csprojPath, IReadOnlyList<string> hostFolders, string? webBootPath = null,
        bool upgradeTargetFramework = true)
    {
        var warnings = new List<string>();
        var added = new List<string>();
        var doc = MsBuildXml.Load(csprojPath);
        var root = doc.Root ?? throw new ToolException($"{csprojPath}: not a valid MSBuild project file");
        var ns = root.Name.Namespace;

        var sdk = (string?)root.Attribute("Sdk");
        if (sdk == null || !sdk.StartsWith("Godot.NET.Sdk", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"{Path.GetFileName(csprojPath)} does not use Godot.NET.Sdk (Sdk=\"{sdk}\"); patching anyway - review the result.");
        else if (VersionRewriter.GodotSdkVersion($"Sdk=\"{sdk}\"") is { } version && CompareVersions(version, ToolVersions.GodotSdkVersion) < 0)
            warnings.Add($"{Path.GetFileName(csprojPath)} uses Godot.NET.Sdk/{version}, older than the {ToolVersions.GodotSdkVersion} this tool targets; not changed - consider upgrading.");

        // Deliberately includes conditioned PropertyGroups: a property set anywhere (even per-configuration) is
        // the author's choice and is left alone rather than overridden with an unconditional duplicate.
        var properties = root.Descendants(ns + "PropertyGroup").Elements().ToList();

        string? Existing(string name) =>
            properties.FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        var patch = new XElement(ns + "PropertyGroup");

        var targetFrameworks = properties.Where(e => e.Name.LocalName == "TargetFramework").ToList();
        if (targetFrameworks.Count == 0)
        {
            patch.Add(Element(ns, "TargetFramework", TargetFramework));
            added.Add($"TargetFramework: {TargetFramework}");
        }
        else if (upgradeTargetFramework && targetFrameworks.Any(e => e.Value != TargetFramework))
        {
            foreach (var targetFramework in targetFrameworks)
                targetFramework.Value = TargetFramework;
            added.Add($"TargetFramework: {TargetFramework}");
        }

        if (Existing("EnableDynamicLoading") == null)
        {
            patch.Add(Element(ns, "EnableDynamicLoading", "true"));
            added.Add("EnableDynamicLoading");
        }

        if (Existing("AllowUnsafeBlocks") == null)
        {
            patch.Add(Element(ns, "AllowUnsafeBlocks", "true"));
            added.Add("AllowUnsafeBlocks");
        }

        var defines = properties.Where(e => e.Name.LocalName == "DefineConstants").ToList();
        if (!defines.Any(d => d.Value.Contains("LIBGODOT_ENABLED")))
        {
            patch.Add(Element(ns, "DefineConstants", "$(DefineConstants);LIBGODOT_ENABLED"));
            added.Add("DefineConstants: LIBGODOT_ENABLED");
        }

        var excludes = properties.Where(e => e.Name.LocalName == "DefaultItemExcludes").ToList();
        var missingFolders = hostFolders
            .Where(f => !excludes.Any(e => e.Value.Contains($"{f}/**")))
            .ToList();
        if (missingFolders.Count > 0)
        {
            patch.Add(Element(ns, "DefaultItemExcludes",
                "$(DefaultItemExcludes);" + string.Join(";", missingFolders.Select(f => $"{f}/**"))));
            added.Add($"DefaultItemExcludes: {string.Join(", ", missingFolders)}");
        }

        // An existing Compile item counts as "already wired" only when it names the requested path or an existing
        // file; a stale Exists-guarded include from a no-web scaffold must NOT count (the bootstrap would not compile).
        XElement? bootGroup = null;
        var csprojDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
        var hasBootInclude = root.Descendants(ns + "Compile")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => include?.EndsWith("TwoDogWebBoot.cs", StringComparison.OrdinalIgnoreCase) == true)
            .Any(include => string.Equals(include, webBootPath, StringComparison.OrdinalIgnoreCase)
                            || File.Exists(Path.Combine(csprojDir, include!.Replace('/', Path.DirectorySeparatorChar))));
        if (webBootPath != null && !hasBootInclude)
        {
            var compile = new XElement(ns + "Compile");
            compile.SetAttributeValue("Include", webBootPath);
            // %27 = escaped apostrophe, so a quoted host folder can't break
            // the condition string.
            compile.SetAttributeValue("Condition",
                $"Exists('$(MSBuildThisFileDirectory){webBootPath.Replace("'", "%27")}')");
            bootGroup = new XElement(ns + "ItemGroup",
                new XText("\n        "), compile, new XText("\n    "));
            added.Add($"Compile Include: {webBootPath}");
        }

        if (!patch.HasElements && bootGroup == null && added.Count == 0) return new Result(null, added, warnings);

        if (patch.HasElements)
            MsBuildXml.AppendPropertyGroup(doc, "added by the 2dog tool: properties 2dog hosts need that were not already set",
                patch.Elements().ToList());

        if (bootGroup != null)
            root.Add(
                new XText("    "),
                new XComment(" added by the 2dog tool: compiles the web bootstrap (in the web host folder) into this game assembly "),
                new XText("\n    "),
                bootGroup,
                new XText("\n"));

        return new Result(MsBuildXml.Serialize(doc), added, warnings);
    }

    private static XElement Element(XNamespace ns, string name, string value) => new(ns + name, value);

    private static int CompareVersions(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) ? va.CompareTo(vb) : 0;
}
