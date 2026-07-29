namespace twodog.tests.ToolTests;

// The boot shell ships twice (dotnet-new template and showcase demo). They
// must stay identical apart from the page title, or shell fixes silently land
// in only one copy.
public class WebShellDriftTests
{
    [Fact]
    public void TemplateAndShowcaseShells_AreIdenticalModuloTitle()
    {
        var root = HelperToolTestBed.RepoRoot;
        var template = File.ReadAllText(Path.Combine(
            root, "templates", "twodog", "Company.Product1.web", "wwwroot", "index.html"));
        var showcase = File.ReadAllText(Path.Combine(
            root, "demos", "showcase", "showcase.web", "wwwroot", "index.html"));

        static string Normalize(string html) => System.Text.RegularExpressions.Regex.Replace(
            html.ReplaceLineEndings("\n"), "<title>.*?</title>", "<title/>");

        Assert.Equal(Normalize(template), Normalize(showcase));
    }
}
