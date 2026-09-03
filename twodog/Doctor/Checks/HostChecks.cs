using System.Xml.Linq;

namespace twodog.cli;

/// <summary>Every host csproj: the marker files and properties the engine and the Godot editor depend on.</summary>
internal static class HostChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("host.gdignore", Category.Hosts, "every host folder carries a .gdignore"),
        new("host.project-reference", Category.Hosts, "every ProjectReference points at an existing project"),
        new("host.godot-project-dir", Category.Hosts, "GodotProjectDir points at the Godot project"),
        new("host.variant", Category.Hosts, "TwoDogVariant is release, debug or editor"),
        new("host.buildtype-deprecated", Category.Hosts, "the deprecated TwoDogBuildType property is gone"),
        new("host.publish-aot", Category.Hosts, "no desktop host enables PublishAot or PublishSingleFile"),
        new("host.duplicate-analyzers", Category.Hosts, "hosts referencing the game strip the duplicate Godot analyzers"),
        new("host.app-manifest", Category.Hosts, "the app.manifest a host declares exists"),
        new("host.web-props-shim", Category.Hosts, "browser hosts chain to the root Directory.Build.props"),
        new("host.web-global-json", Category.Hosts, "browser hosts carry their own global.json"),
        new("host.webboot-drift", Category.Hosts, "the tool-owned TwoDogWebBoot.cs matches this tool's copy"),
        new("host.trimmer-root", Category.Hosts, "browser hosts root the game assembly for the trimmer"),
        new("host.blazor-client", Category.Hosts, "the Blazor client project exists and publishes 2dog.engine"),
        new("host.windows-only", Category.Hosts, "Windows-only hosts are noted on other platforms"),
    ];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.Hosts;
        var p = ctx.Project;
        if (p.Hosts.Count == 0) yield break;
        var issues = new HashSet<string>();
        Finding Issue(Finding f) { issues.Add(f.Id); return f; }

        foreach (var host in p.Hosts)
        {
            var dir = Path.Combine(p.Dir, host.Folder);
            var csproj = $"{host.Folder}/{host.Folder}.csproj";

            if (!host.HasGdIgnore)
                yield return Issue(new Finding("host.gdignore", c, Severity.Fail, $"{host.Folder}/.gdignore missing",
                    "without it the Godot editor imports the host's sources and outputs", null, $"{host.Folder}/.gdignore",
                    new Fix($"gdignore:{host.Folder}", FixClass.Safe, $"create {host.Folder}/.gdignore",
                        () => WriteTemplateFile(p, host, ".gdignore"))));

            if (host.Doc == null) continue;

            foreach (var include in host.Doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference")
                         .Select(e => (string?)e.Attribute("Include")).OfType<string>())
            {
                var target = Path.GetFullPath(Path.Combine(dir, include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(target))
                    yield return Issue(new Finding("host.project-reference", c, Severity.Fail, $"{csproj} references a missing project: {include}",
                        p.BaseName is { } n ? $"the game project is ../{n}.csproj" : null, "fix the ProjectReference (a renamed game project leaves these behind)", csproj));
            }

            // One fix per host csproj: every missing property lands in a single appended PropertyGroup.
            var godotDir = host.Kind == HostKind.Blazor ? null : host.Property("GodotProjectDir");
            var referencesGame = p.BaseName is { } baseName && host.Doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Any(e => ((string?)e.Attribute("Include") ?? "").EndsWith($"{baseName}.csproj", StringComparison.OrdinalIgnoreCase));
            var analyzers = host.Properties("TwoDogRemoveDuplicateGodotAnalyzers").Select(e => e.Value.Trim()).ToList();
            var missingProperties = new List<(string Name, string Value)>();
            if (host.Kind != HostKind.Blazor && godotDir == null) missingProperties.Add(("GodotProjectDir", ".."));
            if (referencesGame && analyzers.Count == 0) missingProperties.Add(("TwoDogRemoveDuplicateGodotAnalyzers", "true"));
            var propertiesFix = missingProperties.Count == 0
                ? null
                : new Fix($"csproj:{host.Folder}", FixClass.Safe,
                    $"add {string.Join(" and ", missingProperties.Select(m => $"<{m.Name}>{m.Value}</{m.Name}>"))} to {csproj}",
                    () => AddProperties(host.CsprojPath, missingProperties.ToArray()));

            if (host.Kind != HostKind.Blazor)
            {
                if (godotDir == null)
                    yield return Issue(new Finding("host.godot-project-dir", c, Severity.Fail, $"{csproj} has no GodotProjectDir",
                        "the 2dog targets import and export the Godot project it names", null, csproj, propertiesFix));
                else if (!godotDir.Contains("$(") && !SamePath(Path.Combine(dir, godotDir), p.Dir))
                    yield return Issue(new Finding("host.godot-project-dir", c, Severity.Fail, $"{csproj} GodotProjectDir '{godotDir}' does not point at this project",
                        null, "set <GodotProjectDir>..</GodotProjectDir>", csproj));
            }

            foreach (var variant in host.Properties("TwoDogVariant").Select(e => e.Value.Trim()).Where(v => !v.Contains("$(")))
                if (variant is not ("release" or "debug" or "editor"))
                    yield return Issue(new Finding("host.variant", c, Severity.Fail, $"{csproj} sets TwoDogVariant '{variant}'",
                        "allowed: release, debug, editor (the build stops with the same message)", "fix the value", csproj));

            if (host.HasProperty("TwoDogBuildType"))
                yield return Issue(new Finding("host.buildtype-deprecated", c, Severity.Warn, $"{csproj} sets the deprecated TwoDogBuildType",
                    "ignored since the API layout follows TwoDogVariant", null, csproj,
                    new Fix($"csproj:{host.Folder}:buildtype", FixClass.Safe, $"remove TwoDogBuildType from {csproj}",
                        () => RemoveProperties(host.CsprojPath, "TwoDogBuildType"))));

            if (!host.IsWebLike)
                foreach (var property in new[] { "PublishAot", "PublishSingleFile" })
                    if (host.Properties(property).Any(e => e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))
                        yield return Issue(new Finding("host.publish-aot", c, Severity.Fail, $"{csproj} enables {property}",
                            "the engine loads GodotPlugins and the game assembly from disk through hostfxr, which a single binary cannot carry",
                            $"remove {property} and publish as a folder (dotnet publish -c Release -r <rid>)", csproj));

            if (referencesGame && analyzers.Count == 0)
                yield return Issue(new Finding("host.duplicate-analyzers", c, Severity.Warn, $"{csproj} lacks TwoDogRemoveDuplicateGodotAnalyzers",
                    "the game's Godot source generators run twice otherwise (duplicate-type warnings)", null, csproj, propertiesFix));
            // Every definition must be true: a per-configuration false lets the duplicates back into that configuration.
            else if (referencesGame && analyzers.FirstOrDefault(v => !v.Contains("$(") && !v.Equals("true", StringComparison.OrdinalIgnoreCase)) is { } off)
                yield return Issue(new Finding("host.duplicate-analyzers", c, Severity.Warn, $"{csproj} sets TwoDogRemoveDuplicateGodotAnalyzers to '{off}'",
                    "the game's Godot source generators run twice unless it is true (duplicate-type warnings)", "set it to true", csproj));

            if (host.Property("ApplicationManifest") is { } manifest && !manifest.Contains("$(") && !File.Exists(Path.Combine(dir, manifest)))
                yield return Issue(new Finding("host.app-manifest", c, Severity.Warn, $"{csproj} declares ApplicationManifest {manifest}, which does not exist",
                    host.Kind == HostKind.Avalonia ? "without the DPI declaration Avalonia renders oversized or clipped" : null,
                    "restore the file or drop the property", csproj));

            if (host.IsWebLike)
            {
                if (p.HasRootBuildProps && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
                    yield return Issue(new Finding("host.web-props-shim", c, Severity.Warn, $"{host.Folder}/Directory.Build.props missing",
                        "the host would shadow the root Directory.Build.props and lose the shared versions", null, $"{host.Folder}/Directory.Build.props",
                        new Fix($"create:{host.Folder}/Directory.Build.props", FixClass.Safe, $"create {host.Folder}/Directory.Build.props (chains to the root)",
                            () => WriteTemplateFile(p, host, "Directory.Build.props"))));

                if (!File.Exists(Path.Combine(dir, "global.json")))
                    yield return Issue(new Finding("host.web-global-json", c, Severity.Info, $"{host.Folder}/global.json missing",
                        "publishing from inside the folder would not pin the SDK", null, $"{host.Folder}/global.json",
                        new Fix($"create:{host.Folder}/global.json", FixClass.Safe, $"create {host.Folder}/global.json",
                            () => WriteTemplateFile(p, host, "global.json"))));

                var boot = Path.Combine(dir, "TwoDogWebBoot.cs");
                if (File.Exists(boot) && File.ReadAllText(boot) != TemplateAssets.WebBootSource())
                    yield return Issue(new Finding("host.webboot-drift", c, Severity.Warn, $"{host.Folder}/TwoDogWebBoot.cs differs from this tool's bootstrap",
                        "the file is tool-owned; an older copy may miss what newer packages expect", "2dog update", $"{host.Folder}/TwoDogWebBoot.cs",
                        new Fix($"refresh:{host.Folder}/TwoDogWebBoot.cs", FixClass.Announced, $"refresh {host.Folder}/TwoDogWebBoot.cs (overwrites the tool-owned file)",
                            () => File.WriteAllText(boot, TemplateAssets.WebBootSource()))));

                if (host.Kind != HostKind.Blazor && p.BaseName is { } game)
                {
                    var roots = host.Doc.Descendants().Where(e => e.Name.LocalName == "TrimmerRootAssembly")
                        .Select(e => (string?)e.Attribute("Include") ?? "").ToList();
                    if (!roots.Any(r => r == game || r.Contains("$(")))
                        yield return Issue(new Finding("host.trimmer-root", c, Severity.Warn,
                            roots.Count > 0
                                ? $"{csproj} roots {string.Join(", ", roots)} but not the game assembly {game}"
                                : $"{csproj} has no TrimmerRootAssembly for the game assembly {game}",
                            "the trimmer may drop the game's types (they are reached by reflection only)", $"add <TrimmerRootAssembly Include=\"{game}\" />", csproj));
                }
            }

            if (host.Kind == HostKind.Blazor)
            {
                if (host.ClientText is not { } client)
                    yield return Issue(new Finding("host.blazor-client", c, Severity.Warn, $"{host.Folder} has no Client/{host.Folder}.Client.csproj",
                        "the Blazor host is a server plus a WebAssembly client that links Godot", "2dog add --blazor <folder> to see the pair", host.Folder));
                else if (!System.Text.RegularExpressions.Regex.IsMatch(client, @"Include=""2dog\.engine""(?=[^>]*PrivateAssets=""all"")(?=[^>]*Publish=""true"")"))
                    yield return Issue(new Finding("host.blazor-client", c, Severity.Warn, $"{host.Folder}/Client: 2dog.engine needs PrivateAssets=\"all\" Publish=\"true\"",
                        "without Publish=\"true\" the trimmer fails with IL1035", "set both attributes on the 2dog.engine PackageReference"));
            }

            if (host.Kind is HostKind.WinForms or HostKind.WinUi && !ctx.Env.IsWindows)
                yield return Issue(new Finding("host.windows-only", c, Severity.Info, $"{host.Folder} ({Hosts.Label(host.Kind)}) only builds on Windows"));
        }

        foreach (var (id, title) in new[]
                 {
                     ("host.gdignore", ".gdignore"), ("host.project-reference", "references"), ("host.godot-project-dir", "GodotProjectDir"),
                     ("host.variant", "variants"), ("host.duplicate-analyzers", "analyzers"),
                 })
            if (!issues.Contains(id))
                yield return Finding.Pass(id, c, title);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void WriteTemplateFile(ProjectModel p, HostModel host, string fileName)
    {
        var file = TemplateAssets.HostFiles(host.Kind, p.BaseName ?? host.Folder, host.Folder)
            .First(f => f.RelativePath == $"{host.Folder}/{fileName}");
        File.WriteAllBytes(Path.Combine(p.Dir, file.RelativePath), file.Content);
    }

    /// <summary>Appends a marked PropertyGroup with the given properties; whitespace elsewhere survives.</summary>
    internal static void AddProperties(string csprojPath, params (string Name, string Value)[] properties)
    {
        var doc = MsBuildXml.Load(csprojPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        MsBuildXml.AppendPropertyGroup(doc, "added by 2dog doctor", properties.Select(p => new XElement(ns + p.Name, p.Value)));
        MsBuildXml.Write(csprojPath, MsBuildXml.Serialize(doc));
    }

    /// <summary>Removes every element with the given local name (and the whitespace node before it).</summary>
    internal static void RemoveProperties(string csprojPath, string name)
    {
        var doc = MsBuildXml.Load(csprojPath);
        foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == name).ToList())
        {
            if (element.PreviousNode is XText { Value: var ws } previous && ws.Trim().Length == 0) previous.Remove();
            element.Remove();
        }

        MsBuildXml.Write(csprojPath, MsBuildXml.Serialize(doc));
    }
}
