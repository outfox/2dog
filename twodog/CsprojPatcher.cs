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

    /// <summary>Packages that work with either engine precision, so the host must reference its engine itself.</summary>
    internal static readonly string[] PrecisionNeutralPackages = ["2dog.xunit", "2dog.nunit", "2dog.repl", "2dog.avalonia", "2dog.qt"];

    /// <summary>
    /// The precision-neutral package a host references without any 2dog.engine, or null. Hosts scaffolded before
    /// the precision split relied on it bringing the engine along.
    /// </summary>
    internal static string? MissingEngineFor(XDocument doc)
    {
        var includes = doc.Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "").ToList();
        // Prefix match: 2dog.engine, 2dog.engine.double and 2dog.engine$(TwoDogPackageSuffix) all count.
        if (includes.Any(i => i.StartsWith("2dog.engine", StringComparison.OrdinalIgnoreCase))) return null;
        return PrecisionNeutralPackages.FirstOrDefault(id => includes.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The csproj with a 2dog.engine reference added next to its precision-neutral package; null if none is missing.</summary>
    internal static string? PatchEngineReference(string csprojPath, bool doublePrecision)
    {
        var doc = MsBuildXml.Load(csprojPath);
        if (MissingEngineFor(doc) is not { } neutral) return null;
        var library = doc.Descendants().First(e => e.Name.LocalName == "PackageReference"
            && string.Equals((string?)e.Attribute("Include"), neutral, StringComparison.OrdinalIgnoreCase));
        var whitespace = (library.PreviousNode as XText)?.Value ?? "";
        var indent = "\n" + whitespace[(whitespace.LastIndexOf('\n') + 1)..];
        library.AddAfterSelf(new XText(indent), new XElement(library.Name,
            new XAttribute("Include", "2dog.engine" + (doublePrecision ? ".double" : "")),
            new XAttribute("Version", (string?)library.Attribute("Version") ?? "$(TwoDogVersion)")));
        return MsBuildXml.Serialize(doc);
    }

    private static XElement Element(XNamespace ns, string name, string value) => new(ns + name, value);
}
