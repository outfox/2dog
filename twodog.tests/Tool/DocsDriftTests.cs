using twodog.cli;

namespace twodog.tests.ToolTests;

// The command tree is the single source of truth; the package README and the docs reference must mention every
// advertised verb and option, and the doctor page every check id.
public class DocsDriftTests
{
    private static string Doc(string relative)
    {
        var path = Path.Combine(HelperToolTestBed.RepoRoot, relative);
        Assert.SkipWhen(!File.Exists(path), $"{relative} not available (packaged run)");
        return File.ReadAllText(path);
    }

    [Theory]
    [InlineData("twodog/README.md")]
    [InlineData("docs/content/dnx-2dog.md")]
    public void EveryVerbAndOption_IsDocumented(string relative)
    {
        var text = Doc(relative);
        foreach (var name in CliTree.VerbNames)
            Assert.True(text.Contains($"2dog {name}"), $"{relative} does not mention '2dog {name}'");
        foreach (var option in CliTree.AllOptions.Where(o => !o.Hidden))
            Assert.True(text.Contains(option.Name), $"{relative} does not mention {option.Name}");
    }

    [Fact]
    public void DoctorPage_ListsEveryCheckId()
    {
        var text = Doc("docs/content/doctor.md");
        foreach (var check in CheckCatalog.All)
            Assert.True(text.Contains($"`{check.Id}`"), $"doctor.md does not list {check.Id}");
    }

    [Fact]
    public void Sidebar_LinksTheNewPages()
    {
        var config = Doc("docs/.vitepress/config.mts");
        Assert.Contains("link: '/doctor'", config);
        Assert.Contains("link: '/troubleshooting'", config);
    }
}
