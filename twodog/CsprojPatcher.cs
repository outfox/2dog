using System.Xml.Linq;

namespace twodog.cli;

/// <summary>
/// Minimal in-place patching of an existing Godot project csproj: appends one
/// clearly-marked PropertyGroup containing only the properties 2dog needs
/// that are not already present. Existing target frameworks are upgraded in place.
/// </summary>
internal static class CsprojPatcher
{
    private const string TargetFramework = "net10.0";

    public sealed record Result(string? NewContent, List<string> Added, List<string> Warnings);

    public static Result Patch(string csprojPath, IReadOnlyList<string> hostFolders, string? webBootPath = null)
    {
        var warnings = new List<string>();
        var added = new List<string>();
        var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new ToolException($"{csprojPath}: not a valid MSBuild project file");
        var ns = root.Name.Namespace;

        var sdk = (string?)root.Attribute("Sdk");
        if (sdk == null || !sdk.StartsWith("Godot.NET.Sdk", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"{Path.GetFileName(csprojPath)} does not use Godot.NET.Sdk (Sdk=\"{sdk}\"); patching anyway - review the result.");
        else if (SdkVersion(sdk) is { } version && CompareVersions(version, ToolVersions.GodotSdkVersion) < 0)
            warnings.Add($"{Path.GetFileName(csprojPath)} uses Godot.NET.Sdk/{version}, older than the {ToolVersions.GodotSdkVersion} this tool targets; not changed - consider upgrading.");

        // Deliberately includes conditioned PropertyGroups: a property the
        // project sets anywhere (even per-configuration) is treated as "the
        // author made a choice" and left alone rather than overridden with an
        // unconditional duplicate.
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
        else if (targetFrameworks.Any(e => e.Value != TargetFramework))
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

        // The web bootstrap Compile Include: any existing Compile item ending
        // in TwoDogWebBoot.cs counts as "already wired" (idempotence, and
        // custom layouts the author chose are theirs to keep).
        XElement? bootGroup = null;
        var hasBootInclude = root.Descendants(ns + "Compile")
            .Any(e => ((string?)e.Attribute("Include"))?
                .EndsWith("TwoDogWebBoot.cs", StringComparison.OrdinalIgnoreCase) == true);
        if (webBootPath != null && !hasBootInclude)
        {
            var compile = new XElement(ns + "Compile");
            compile.SetAttributeValue("Include", webBootPath);
            compile.SetAttributeValue("Condition", $"Exists('{webBootPath}')");
            bootGroup = new XElement(ns + "ItemGroup",
                new XText("\n        "), compile, new XText("\n    "));
            added.Add($"Compile Include: {webBootPath}");
        }

        if (!patch.HasElements && bootGroup == null && added.Count == 0) return new Result(null, added, warnings);

        // Readable indentation: PreserveWhitespace + DisableFormatting keep
        // the rest of the file byte-identical, so the injected group carries
        // its own whitespace.
        var children = patch.Elements().ToList();
        patch.RemoveNodes();
        foreach (var child in children)
            patch.Add(new XText("\n        "), child);
        patch.Add(new XText("\n    "));

        if (patch.HasElements)
            root.Add(
                new XText("    "),
                new XComment(" added by the 2dog tool: properties 2dog hosts need that were not already set "),
                new XText("\n    "),
                patch,
                new XText("\n"));

        if (bootGroup != null)
            root.Add(
                new XText("    "),
                new XComment(" added by the 2dog tool: compiles the web bootstrap (in the web host folder) into this game assembly "),
                new XText("\n    "),
                bootGroup,
                new XText("\n"));

        // XDocument.ToString drops the XML declaration; put it back if the
        // file had one.
        var text = doc.ToString(SaveOptions.DisableFormatting);
        if (doc.Declaration != null)
            text = doc.Declaration + Environment.NewLine + text;
        return new Result(text, added, warnings);
    }

    private static XElement Element(XNamespace ns, string name, string value) => new(ns + name, value);

    private static string? SdkVersion(string sdk)
    {
        var slash = sdk.IndexOf('/');
        return slash < 0 ? null : sdk[(slash + 1)..];
    }

    private static int CompareVersions(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) ? va.CompareTo(vb) : 0;
}
