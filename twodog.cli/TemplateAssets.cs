using System.Reflection;

namespace twodog.cli;

/// <summary>
/// Access to the dotnet-new template content embedded in this assembly
/// (LogicalName "tpl/..."), with the template's literal rename/version tokens
/// substituted at read time. The embedded files are the single source of
/// truth shared with the 2dog.Templates package.
/// </summary>
internal static class TemplateAssets
{
    private const string SourceName = "Company.Product1";

    /// <summary>
    /// All embedded template resource names, normalized to forward slashes
    /// (MSBuild's %(RecursiveDir) yields the host OS separator at build time).
    /// </summary>
    private static readonly IReadOnlyList<string> Names =
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => Normalize(n).StartsWith("tpl/", StringComparison.Ordinal))
            .ToList();

    private static string Normalize(string resourceName) => resourceName.Replace('\\', '/');

    private static string ReadRaw(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var actual = Names.FirstOrDefault(n => Normalize(n) == name)
                     ?? throw new InvalidOperationException($"Embedded template resource missing: {name}");
        using var stream = assembly.GetManifestResourceStream(actual)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The template's Godot project csproj, tokens substituted.</summary>
    public static string GodotCsproj(string baseName) =>
        Substitute(ReadRaw("tpl/Company.Product1.csproj"), baseName);

    /// <summary>The web bootstrap source, copied verbatim (template copyOnly).</summary>
    public static string WebBootSource() => ReadRaw("tpl/TwoDogWebBoot.cs");

    /// <summary>
    /// Relative target path -> content for every file of a host subtree
    /// ("2dog", "web" or "tests"), tokens substituted in paths and contents.
    /// </summary>
    public static IEnumerable<(string RelativePath, string Content)> HostFiles(string suffix, string baseName)
    {
        var prefix = $"tpl/{SourceName}.{suffix}/";
        foreach (var name in Names.Select(Normalize).Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order())
        {
            // Substitute the rename token in file names too (e.g.
            // Company.Product1.2dog.csproj -> MyGame.2dog.csproj).
            var relative = $"{baseName}.{suffix}/{name[prefix.Length..].Replace(SourceName, baseName)}";
            yield return (relative, Substitute(ReadRaw(name), baseName));
        }
    }

    /// <summary>
    /// Ordered literal replacement of the template tokens. Both rename tokens
    /// resolve to the same base name (the template engine splits them across
    /// files; the sourceName token also matches path fragments, which is
    /// exactly what we substitute in HostFiles paths).
    /// </summary>
    public static string Substitute(string text, string baseName) => text
        .Replace(SourceName, baseName)
        .Replace("TPLRAWNAME", baseName)
        .Replace("TWODOG_PKG_VERSION", ToolVersions.TwoDogVersion)
        .Replace("NATIVES_PKG_VERSION", ToolVersions.NativesVersion)
        .Replace("GODOT_SDK_VERSION", ToolVersions.GodotSdkVersion);
}
