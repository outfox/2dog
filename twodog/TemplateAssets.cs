using System.Reflection;
using System.Text;

namespace twodog.cli;

/// <summary>
/// Access to the dotnet-new template content embedded in this assembly (LogicalName "tpl/...", sourced from
/// templates/twodog), with the template's literal rename/version tokens substituted at read time.
/// </summary>
internal static class TemplateAssets
{
    private const string SourceName = "Company.Product1";

    public sealed record HostFile(string RelativePath, byte[] Content)
    {
        public string Text => Encoding.UTF8.GetString(Content);
    }

    /// <summary>
    /// All embedded template resource names, normalized to forward slashes (%(RecursiveDir) yields the host OS
    /// separator at build time).
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

    private static byte[] ReadRawBytes(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var actual = Names.FirstOrDefault(n => Normalize(n) == name)
                     ?? throw new InvalidOperationException($"Embedded template resource missing: {name}");
        using var stream = assembly.GetManifestResourceStream(actual)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The template's Godot project csproj, tokens substituted.</summary>
    public static string GodotCsproj(string baseName) =>
        Substitute(ReadRaw("tpl/Company.Product1.csproj"), baseName);

    /// <summary>The web bootstrap source, copied verbatim (template copyOnly).</summary>
    public static string WebBootSource() => ReadRaw("tpl/Company.Product1.web/TwoDogWebBoot.cs");

    /// <summary>The template's export_presets.cfg (Web preset), verbatim - no tokens.</summary>
    public static string ExportPresets() => ReadRaw("tpl/export_presets.cfg");

    /// <summary>The template's root global.json (wasm-capable SDK pin), verbatim - no tokens.</summary>
    public static string RootGlobalJson() => ReadRaw("tpl/global.json");

    /// <summary>The template's root MSBuild cleanup target, verbatim.</summary>
    public static string RootBuildTargets() => ReadRaw("tpl/Directory.Build.targets");

    /// <summary>The template's root Directory.Build.props: the 2dog version properties, tokens substituted.</summary>
    public static string RootBuildProps() => Substitute(ReadRaw("tpl/Directory.Build.props"), SourceName);

    /// <summary>
    /// The Godot project files a brand-new project needs on top of the ones above: relative path -> content.
    /// </summary>
    public static IEnumerable<(string RelativePath, string Content)> NewProjectFiles(string baseName)
    {
        yield return ("project.godot", Substitute(ReadRaw("tpl/project.godot"), baseName));
        yield return ("main.tscn", ReadRaw("tpl/main.tscn"));
        yield return (".editorconfig", ReadRaw("tpl/.editorconfig"));
        yield return (".gitignore", ReadRaw("tpl/.gitignore"));
    }

    /// <summary>
    /// Relative target path -> content for every file of a host subtree, with the template's folder name replaced
    /// by the host's actual folder name and the usual rename/version tokens substituted.
    /// </summary>
    public static IEnumerable<HostFile> HostFiles(HostKind kind, string baseName, string folder)
    {
        var sourceFolder = $"{SourceName}.{Hosts.Suffix(kind)}";
        var prefix = $"tpl/{sourceFolder}/";
        // TwoDogWebBoot.cs is excluded: PlanWebBoot is its single writer (one copy per project - two would be
        // CS0101 in the game assembly with several web hosts).
        foreach (var name in Names.Select(Normalize)
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                     .Where(n => !n.EndsWith("/TwoDogWebBoot.cs", StringComparison.Ordinal))
                     .Order())
        {
            // Substitute in file names too (e.g. Company.Product1.2dog.csproj
            // -> MyGame.tools.csproj for a desktop host folder "MyGame.tools").
            var relative = $"{folder}/{Rename(name[prefix.Length..], sourceFolder, folder, baseName)}";
            var content = Path.GetFileName(name).StartsWith("favicon", StringComparison.Ordinal)
                          || name.EndsWith(".min.js", StringComparison.Ordinal)
                ? ReadRawBytes(name)
                : Encoding.UTF8.GetBytes(Rename(ReadRaw(name), sourceFolder, folder, baseName));
            yield return new HostFile(relative, content);
        }
    }

    /// <summary>
    /// Ordered literal replacement: the host folder token first (it starts with the sourceName token, so the
    /// general rename must not run first), then the project-wide tokens.
    /// </summary>
    private static string Rename(string text, string sourceFolder, string folder, string baseName) =>
        Substitute(text.Replace(sourceFolder, folder), baseName);

    /// <summary>
    /// Ordered literal replacement of the template tokens. Both rename tokens resolve to the same base name; the
    /// sourceName token also matches path fragments, which HostFiles relies on.
    /// </summary>
    public static string Substitute(string text, string baseName) => text
        .Replace(SourceName, baseName)
        .Replace("TPLRAWNAME", baseName)
        .Replace("TWODOG_PKG_VERSION", ToolVersions.TwoDogVersion)
        .Replace("NATIVES_PKG_VERSION", ToolVersions.NativesVersion)
        .Replace("GODOT_SDK_VERSION", ToolVersions.GodotSdkVersion)
        .Replace("AVALONIA_PKG_VERSION", ToolVersions.AvaloniaVersion)
        .Replace("WINAPPSDK_PKG_VERSION", ToolVersions.WindowsAppSdkVersion)
        .Replace("ASPNETCORE_PKG_VERSION", ToolVersions.AspNetCoreVersion);
}
