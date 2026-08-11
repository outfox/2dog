namespace twodog.tests.ToolTests;

// The web host ships four times (web/webxr dotnet-new templates and the two
// showcase demo hosts). Each template must stay identical to its showcase
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
    // present (the web copy wins when both exist), so the two copies must not
    // diverge - a webxr-only project would silently boot differently.
    [Theory]
    [InlineData("templates", "twodog", "Company.Product1")]
    [InlineData("demos", "showcase", "showcase")]
    public void WebAndWebXrBootFiles_AreIdentical(params string[] parts)
    {
        var root = parts[..^1];
        var baseName = parts[^1];
        var web = HostFile("TwoDogWebBoot.cs", [.. root, $"{baseName}.web"]);
        var webxr = HostFile("TwoDogWebBoot.cs", [.. root, $"{baseName}.webxr"]);

        Assert.Equal(web.ReplaceLineEndings("\n"), webxr.ReplaceLineEndings("\n"));
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
