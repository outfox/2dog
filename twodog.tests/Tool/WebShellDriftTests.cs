namespace twodog.tests.ToolTests;

// The boot shell ships four times (web/webxr dotnet-new templates and the two
// showcase demo hosts). Each template must stay identical to its showcase
// mirror apart from the page title, and the webxr shell must differ from the
// web shell only by the WebXR Layers polyfill block - or shell fixes silently
// land in only some copies.
public class WebShellDriftTests
{
    private static string Shell(params string[] parts) =>
        File.ReadAllText(Path.Combine([HelperToolTestBed.RepoRoot, .. parts, "wwwroot", "index.html"]));

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
            "         (including desktop Chrome and the Immersive Web Emulator) still lack. -->\n" +
            "    <script src=\"webxr-layers-polyfill.min.js\"></script>\n" +
            "    <script>new WebXRLayersPolyfill();</script>\n";

        var web = Normalize(Shell("templates", "twodog", "Company.Product1.web"));
        var webxr = Normalize(Shell("templates", "twodog", "Company.Product1.webxr"));

        var withoutBlock = webxr.Replace(polyfillBlock, "");
        Assert.NotEqual(webxr, withoutBlock);
        Assert.Equal(web, withoutBlock);
    }
}
