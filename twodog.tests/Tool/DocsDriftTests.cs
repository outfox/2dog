using twodog.cli;

namespace twodog.tests.ToolTests;

// The command tree is the single source of truth; the package README and the docs reference (the index page plus
// one page per verb) must mention every advertised verb and option, and the doctor verb page every check id.
public class DocsDriftTests
{
    private const string VerbPages = "docs/content/cli";

    private static string Doc(string relative)
    {
        var path = Path.Combine(HelperToolTestBed.RepoRoot, relative);
        Assert.SkipWhen(!File.Exists(path), $"{relative} not available (packaged run)");
        return File.ReadAllText(path);
    }

    /// <summary>The docs reference reads as one text: the index page and every verb page under it.</summary>
    private static string Reference(string relative, string? pagesDir) => pagesDir is null
        ? Doc(relative)
        : string.Concat(Directory.GetFiles(Path.Combine(HelperToolTestBed.RepoRoot, pagesDir), "*.md")
            .Select(File.ReadAllText).Prepend(Doc(relative)));

    [Theory]
    [InlineData("twodog/README.md", null)]
    [InlineData("docs/content/dnx-2dog.md", VerbPages)]
    public void EveryVerbAndOption_IsDocumented(string relative, string? pagesDir)
    {
        var text = Reference(relative, pagesDir);
        var where = pagesDir is null ? relative : $"{relative} + {pagesDir}";
        foreach (var name in CliTree.VerbNames)
            Assert.True(text.Contains($"2dog {name}"), $"{where} does not mention '2dog {name}'");
        foreach (var option in CliTree.AllOptions.Where(o => !o.Hidden))
            Assert.True(text.Contains(option.Name), $"{where} does not mention {option.Name}");
    }

    [Fact]
    public void DoctorVerbPage_ListsEveryCheckId()
    {
        var text = Doc($"{VerbPages}/doctor.md");
        foreach (var check in CheckCatalog.All)
            Assert.True(text.Contains($"`{check.Id}`"), $"cli/doctor.md does not list {check.Id}");
    }

    [Fact]
    public void EveryVerb_HasAPage_LinkedFromTheSidebar()
    {
        var config = Doc("docs/.vitepress/config.mts");
        Assert.Contains("link: '/dnx-2dog'", config);
        Assert.Contains("link: '/doctor'", config);
        Assert.Contains("link: '/troubleshooting'", config);
        foreach (var command in CliTree.Root.Subcommands.Where(c => !c.Hidden))
        {
            Doc($"{VerbPages}/{command.Name}.md");
            Assert.Contains($"link: '/cli/{command.Name}'", config);
        }
    }
}
