using System.Xml.Linq;

namespace twodog.cli;

/// <summary>
/// Minimal in-place patching of an existing Godot project csproj: appends one clearly-marked PropertyGroup with the
/// 2dog properties not already present. Target frameworks are upgraded in place (or added) and an older
/// Godot.NET.Sdk is raised to the tool's only when asked, since both change what the project compiles against.
/// </summary>
internal static class CsprojPatcher
{
    private const string TargetFramework = "net10.0";
    public const string BindingsAdded = "PackageReference: 2dog.godotsharp";

    public sealed record Result(string? NewContent, List<string> Added, List<string> Warnings);

    public static Result Patch(string csprojPath, IReadOnlyList<string> hostFolders,
        bool upgradeTargetFramework = true, bool upgradeGodotSdk = false)
    {
        var warnings = new List<string>();
        var added = new List<string>();
        var doc = MsBuildXml.Load(csprojPath);
        var root = doc.Root ?? throw new ToolException($"{csprojPath}: not a valid MSBuild project file");
        var ns = root.Name.Namespace;

        var sdk = (string?)root.Attribute("Sdk");
        if (sdk == null || !sdk.StartsWith("Godot.NET.Sdk", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"{Path.GetFileName(csprojPath)} does not use Godot.NET.Sdk (Sdk=\"{sdk}\"); patching anyway - review the result.");
        else if (VersionRewriter.GodotSdkVersion($"Sdk=\"{sdk}\"") is { } version && VersionRewriter.IsOlderGodotSdk(version))
        {
            if (upgradeGodotSdk)
            {
                // The 2dog packages are built for one Godot line: the hosts being added would not build against an older SDK.
                root.SetAttributeValue("Sdk", $"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}");
                added.Add($"Godot.NET.Sdk: {version} -> {ToolVersions.GodotSdkVersion}");
                if (VersionRewriter.GodotLineChangeWarning(version, ToolVersions.GodotSdkVersion) is { } warning)
                    warnings.Add(warning);
            }
            else
                warnings.Add($"{Path.GetFileName(csprojPath)} uses Godot.NET.Sdk/{version}, older than the {ToolVersions.GodotSdkVersion} this tool targets; not changed - consider upgrading.");
        }

        // Deliberately includes conditioned PropertyGroups: a property set anywhere (even per-configuration) is
        // the author's choice and is left alone rather than overridden with an unconditional duplicate.
        var properties = root.Descendants(ns + "PropertyGroup").Elements().ToList();

        string? Existing(string name) =>
            properties.FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        var patch = new XElement(ns + "PropertyGroup");

        var targetFrameworks = properties.Where(e => e.Name.LocalName == "TargetFramework").ToList();
        if (targetFrameworks.Count == 0 && upgradeTargetFramework)
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

        if (EnsureBindings(doc)) added.Add(BindingsAdded);

        if (!patch.HasElements && added.Count == 0) return new Result(null, added, warnings);

        if (patch.HasElements)
            MsBuildXml.AppendPropertyGroup(doc, "added by the 2dog tool: properties 2dog hosts need that were not already set",
                patch.Elements().ToList());

        return new Result(MsBuildXml.Serialize(doc), added, warnings);
    }

    // The host's dependencies cannot flow backwards into the game. Add the managed package when
    // adapting an existing game, just as the new-project template does. The package handles SDK defaults.
    internal static string? PatchBindings(string csprojPath)
    {
        var doc = MsBuildXml.Load(csprojPath);
        return EnsureBindings(doc) ? MsBuildXml.Serialize(doc) : null;
    }

    private static bool EnsureBindings(XDocument doc)
    {
        var root = doc.Root!;
        var ns = root.Name.Namespace;
        var changed = false;
        var doublePrecision = root.Descendants(ns + "PackageReference").Any(e =>
            ((string?)e.Attribute("Include"))?.Equals("2dog.godotsharp.double", StringComparison.OrdinalIgnoreCase) == true);
        foreach (var id in new[] { "2dog.godotsharp", "2dog.godotsharp.editor" })
        {
            var packageId = id + (doublePrecision ? ".double" : "");
            // A property-selected precision suffix (2dog.godotsharp$(Suffix)) counts as present.
            if (root.Descendants(ns + "PackageReference").Select(e => (string?)e.Attribute("Include") ?? "").Any(include =>
                    string.Equals(include, packageId, StringComparison.OrdinalIgnoreCase)
                    || include.StartsWith(id + "$(", StringComparison.OrdinalIgnoreCase))) continue;
            var reference = new XElement(ns + "PackageReference",
                new XAttribute("Include", packageId), new XAttribute("Version", "$(TwoDogVersion)"));
            if (id.EndsWith(".editor", StringComparison.Ordinal))
                reference.SetAttributeValue("Condition", "'$(Configuration)' == 'Debug' Or '$(Configuration)' == 'Editor'");
            root.Add(new XText("    "), new XElement(ns + "ItemGroup",
                new XText("\n        "), reference, new XText("\n    ")), new XText("\n"));
            changed = true;
        }
        return changed;
    }

    private static XElement Element(XNamespace ns, string name, string value) => new(ns + name, value);
}
