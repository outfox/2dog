namespace twodog.tests.ToolTests;

// The web host ships several times (web/webxr/blazor dotnet-new templates and
// their showcase demo hosts). Each template must stay identical to its showcase
// mirror apart from the page title, and the webxr flavor must not drift from
// the web flavor: the shell differs only by the WebXR Layers polyfill block,
// TwoDogWebBoot.cs is byte-identical, and the template Program.cs differs
// only by the host label - or fixes silently land in only some copies.
public class WebShellDriftTests
{
    private static string HostFile(string file, params string[] parts) =>
        File.ReadAllText(Path.Combine([HelperToolTestBed.RepoRoot, .. parts, .. file.Split('/')]));

    private static string Shell(params string[] parts) => HostFile("wwwroot/index.html", parts);

    private static string Normalize(string html) => System.Text.RegularExpressions.Regex.Replace(
        html.ReplaceLineEndings("\n"), "<title>.*?</title>", "<title/>");

    [Theory]
    [InlineData("web")]
    [InlineData("webxr")]
    public void TemplateAndShowcaseShells_AreIdenticalModuloTitle(string suffix)
    {
        var template = Shell("templates", "twodog", $"Company.Product1.{suffix}");
        var showcase = Shell("demos", "showcase", $"showcase.{suffix}");

        Assert.Equal(Normalize(template), Normalize(showcase));
    }

    [Fact]
    public void WebXrShell_IsTheWebShellPlusThePolyfillBlock()
    {
        const string polyfillBlock =
            "    <!-- Godot's WebXR support requires the WebXR Layers API, which most browsers\n" +
            "         (including desktop Chrome and the Immersive Web Emulator) still lack.\n" +
            "         The polyfill constructor throws without navigator.xr, so skip it there\n" +
            "         (no WebXR at all means no session to polyfill) and record the outcome\n" +
            "         for smoke checks. -->\n" +
            "    <script src=\"webxr-layers-polyfill.min.js\"></script>\n" +
            "    <script>\n" +
            "        if (navigator.xr) {\n" +
            "            new WebXRLayersPolyfill();\n" +
            "            document.documentElement.setAttribute('data-twodog-xr-layers', 'polyfilled');\n" +
            "        } else {\n" +
            "            document.documentElement.setAttribute('data-twodog-xr-layers', 'unavailable');\n" +
            "        }\n" +
            "    </script>\n";

        var web = Normalize(Shell("templates", "twodog", "Company.Product1.web"));
        var webxr = Normalize(Shell("templates", "twodog", "Company.Product1.webxr"));

        // Exactly once: the shell is the web shell plus ONE polyfill block, so
        // an accidental second copy must fail here rather than be stripped too.
        var index = webxr.IndexOf(polyfillBlock, StringComparison.Ordinal);
        Assert.True(index >= 0, "the webxr shell does not contain the expected polyfill block");
        Assert.Equal(-1, webxr.IndexOf(polyfillBlock, index + 1, StringComparison.Ordinal));

        Assert.Equal(web, webxr.Remove(index, polyfillBlock.Length));
    }

    // The game csproj compiles whichever host folder's TwoDogWebBoot.cs is
    // present (web wins over webxr over blazor), so the copies must not
    // diverge - a webxr- or blazor-only project would silently boot differently.
    [Theory]
    [InlineData("webxr", "templates", "twodog", "Company.Product1")]
    [InlineData("webxr", "demos", "showcase", "showcase")]
    [InlineData("blazor", "templates", "twodog", "Company.Product1")]
    [InlineData("blazor", "demos", "showcase", "showcase")]
    public void WebFlavorBootFiles_AreIdentical(string suffix, params string[] parts)
    {
        var root = parts[..^1];
        var baseName = parts[^1];
        var web = HostFile("TwoDogWebBoot.cs", [.. root, $"{baseName}.web"]);
        var flavor = HostFile("TwoDogWebBoot.cs", [.. root, $"{baseName}.{suffix}"]);

        Assert.Equal(web.ReplaceLineEndings("\n"), flavor.ReplaceLineEndings("\n"));
    }

    // The Blazor host ships twice (template + showcase); everything but the
    // app-specific page must match once the root namespace is aligned.
    [Theory]
    [InlineData("Components/App.razor")]
    [InlineData("Components/Routes.razor")]
    [InlineData("Program.cs")]
    [InlineData("wwwroot/app.css")]
    [InlineData("Client/Program.cs")]
    public void TemplateAndShowcaseBlazorHosts_ShareTheirShell(string file)
    {
        var template = HostFile(file, "templates", "twodog", "Company.Product1.blazor")
            .Replace("Company.Product1.Blazor", "showcase.blazor");
        var showcase = HostFile(file, "demos", "showcase", "showcase.blazor");

        Assert.Equal(template.ReplaceLineEndings("\n"), showcase.ReplaceLineEndings("\n"));
    }

    // The showcase Programs deliberately differ (the webxr flavor asserts the
    // WebXR interface for CI), but the template Programs must stay in
    // lockstep apart from the host label in the startup line.
    [Fact]
    public void TemplateWebAndWebXrPrograms_DifferOnlyByHostLabel()
    {
        var web = HostFile("Program.cs", "templates", "twodog", "Company.Product1.web").ReplaceLineEndings("\n");
        var webxr = HostFile("Program.cs", "templates", "twodog", "Company.Product1.webxr").ReplaceLineEndings("\n");

        Assert.Equal(web, webxr.Replace("(webxr) starting", "(web) starting"));
    }
}
