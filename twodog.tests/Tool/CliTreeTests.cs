using System.CommandLine;
using twodog.cli;

namespace twodog.tests.ToolTests;

// The System.CommandLine-backed parser: the command tree is the single source of truth for options and help, so
// these lock the tree, the pre-passes around it and the wording of usage errors.
public class CliTreeTests
{
    [Fact]
    public void EveryOption_ParsesUnderEveryVerbItBelongsTo()
    {
        foreach (var command in CliTree.Commands.Where(c => c != CliTree.Root && c.Subcommands.Count == 0))
        {
            var (path, _) = CliTree.Resolve(PathTo(command));
            foreach (var option in CliTree.OptionsOf(path).Where(o => o != CliTree.Pin && o != CliTree.To))
            foreach (var spelling in CliTree.NamesOf(option))
            {
                var args = PathTo(command).Concat(ArgsFor(command)).Append(spelling)
                    .Concat(option.ValueType == typeof(bool) || CliTree.HostKindOf(option) != null ? [] : ["value"])
                    .ToArray();
                var parsed = CommandLine.Parse(args);
                var expected = option == CliTree.HelpOption ? Verb.Help
                    : option == CliTree.VersionOption ? Verb.Version
                    : CliTree.VerbOf(command);
                Assert.Equal(expected, parsed.Verb);
            }
        }
    }

    private static string[] PathTo(Command command) =>
        command == CliTree.PackList ? ["pack", "list"] : [command.Name];

    private static string[] ArgsFor(Command command) =>
        command == CliTree.PackList ? ["some.pck"] : [];

    [Theory]
    [InlineData("new", "--rename", "X")]
    [InlineData("add", "--output", "X")]
    [InlineData("pack", "list", "x.pck", "--desktop")]
    [InlineData("version", "--dry-run")]
    public void Option_OutsideItsVerb_NamesTheVerbsItBelongsTo(params string[] args)
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(args));
        Assert.Contains("is not an option of '2dog", ex.Message);
        Assert.Contains("applies to", ex.Message);
    }

    [Fact]
    public void RenameOnNew_SaysWhy()
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(["new", "X", "--rename", "Y"]));
        Assert.Contains("add/convert only", ex.Message);
        Assert.Contains("already picks a clean name", ex.Message);
    }

    [Theory]
    [InlineData("--dekstop", "--desktop")]
    [InlineData("--tets", "--tests")]
    [InlineData("--dry", "--dry-run")]
    [InlineData("--forc", "--force")]
    public void UnknownOption_SuggestsTheClosestOne(string typo, string suggestion)
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(["add", typo]));
        Assert.Contains($"unknown option '{typo}'", ex.Message);
        Assert.Contains($"did you mean {suggestion}?", ex.Message);
        Assert.Equal(Verb.Add, ex.Verb);
    }

    [Fact]
    public void UnknownOption_WithoutACloseMatch_StaysSilent()
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(["add", "--zzzzzzzz"]));
        Assert.Equal("unknown option '--zzzzzzzz'", ex.Message);
    }

    [Theory]
    [InlineData("ad", "add")]
    [InlineData("nwe", "new")]
    [InlineData("pakc", "pack")]
    [InlineData("convrt", "convert")]
    public void UnknownVerb_SuggestsTheClosestOne(string typo, string suggestion)
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse([typo]));
        Assert.Contains($"unknown verb '{typo}' (did you mean {suggestion}?)", ex.Message);
    }

    [Fact]
    public void PackOperationTypo_SuggestsList()
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(["pack", "lst", "x.pck"]));
        Assert.Contains("unknown pack operation 'lst'", ex.Message);
    }

    [Fact]
    public void DoubleDash_EndsOptionParsing()
    {
        var cmd = CommandLine.Parse(["add", "--", "--weird-dir"]);
        Assert.Equal("--weird-dir", cmd.Options.ProjectPath);
        Assert.Empty(cmd.Requested);
    }

    [Theory]
    [InlineData("--name=Foo")]
    [InlineData("--name:Foo")]
    public void OptionValues_AcceptTheAttachedForms(string arg)
    {
        Assert.Equal("Foo", CommandLine.Parse(["add", arg]).Options.NameOverride);
    }

    [Fact]
    public void HostFolder_AcceptsTheAttachedForm()
    {
        var cmd = CommandLine.Parse(["add", "--desktop=Tools", "--web"]);
        Assert.Equal([(HostKind.Desktop, "Tools"), (HostKind.Web, (string?)null)],
            cmd.Requested.Select(r => (r.Kind, r.Folder)).ToArray());
    }

    [Fact]
    public void Pin_IsAUsageErrorEverywhere()
    {
        foreach (var args in new[] { new[] { "--pin", "1.2.3" }, ["add", "--pin"], ["new", "X", "--pin", "4.7.2.1"] })
        {
            var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(args));
            Assert.Contains("cannot pin its own version", ex.Message);
            Assert.Contains(args[^1] == "--pin" ? "dnx 2dog@<version>" : $"dnx 2dog@{args[^1]}", ex.Message);
        }
    }

    [Fact]
    public void ExcludingAnOptInKind_DoesNotCountAsAHostChoice()
    {
        var cmd = CommandLine.Parse(["add", "--no-winui"]);
        Assert.False(cmd.HostFlagsSeen);
        Assert.Contains(HostKind.WinUi, cmd.Excluded);
        Assert.Contains(cmd.Notes, n => n.Contains("--no-winui changes nothing"));

        Assert.True(CommandLine.Parse(["add", "--no-web"]).HostFlagsSeen);
        Assert.Empty(CommandLine.Parse(["add", "--no-web"]).Notes);
    }

    [Fact]
    public void HelpVerb_IsCarriedThrough()
    {
        Assert.Null(CommandLine.Parse(["help"]).HelpVerb);
        Assert.Null(CommandLine.Parse(["--help"]).HelpVerb);
        Assert.Equal(Verb.New, CommandLine.Parse(["help", "new"]).HelpVerb);
        Assert.Equal(Verb.Add, CommandLine.Parse(["help", "convert"]).HelpVerb);
        Assert.Equal(Verb.Add, CommandLine.Parse(["add", "--help"]).HelpVerb);
        Assert.Equal(Verb.Pack, CommandLine.Parse(["pack", "list", "-h"]).HelpVerb);
        Assert.Throws<UsageException>(() => CommandLine.Parse(["help", "bogus"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0, "help")]
    [InlineData(0, "new", "--help")]
    [InlineData(0, "help", "pack")]
    [InlineData(1, "--unknown")]
    [InlineData(1, "--pin", "1.2.3")]
    [InlineData(1, "ad")]
    [InlineData(1, "new", "--desktop")]
    [InlineData(1, "add", "--dekstop")]
    [InlineData(1, "pack")]
    [InlineData(1, "pack", "list")]
    [InlineData(1, "new", "a", "b", "c")]
    public void ExitCodes_FollowTheTable(int expected, params string[] args)
    {
        var run = CliConsole.Run(args);
        Assert.Equal(expected, run.ExitCode);
        if (expected == 1)
        {
            Assert.StartsWith("error:", run.Stderr);
            Assert.Contains("hint: see '2dog", run.Stderr);
            Assert.Equal("", run.Stdout);
        }
    }
}

public class UsageTests
{
    [Fact]
    public void GeneralHelp_ListsEveryVerbAndEveryAdvertisedOption()
    {
        var text = Usage.Render(null);
        foreach (var name in CliTree.VerbNames) Assert.Contains($"  {name} ", text + " ");
        foreach (var option in CliTree.AllOptions.Where(o => !o.Hidden))
            Assert.Contains(option.Name, text);
        Assert.DoesNotContain("--pin", text);
        Assert.DoesNotContain("--no-winui", text);
    }

    [Fact]
    public void VerbHelp_ShowsOnlyThatVerbsOptions()
    {
        var newHelp = Usage.Render(Verb.New);
        Assert.StartsWith("usage: 2dog new [Name] [dir] [hosts] [options]", newHelp);
        Assert.Contains("--output", newHelp);
        Assert.DoesNotContain("--rename", newHelp);

        var addHelp = Usage.Render(Verb.Add);
        Assert.StartsWith("usage: 2dog add [path] [hosts] [options]", addHelp);
        Assert.Contains("--rename", addHelp);
        Assert.DoesNotContain("--output", addHelp);

        var packHelp = Usage.Render(Verb.Pack);
        Assert.StartsWith("usage: 2dog pack list <pck>", packHelp);
        Assert.DoesNotContain("--desktop", packHelp);
    }

    [Fact]
    public void HelpVerb_AndHelpFlag_RenderTheSameText()
    {
        Assert.Equal(CliConsole.Run("help", "new").Stdout, CliConsole.Run("new", "--help").Stdout);
        Assert.Equal(CliConsole.Run("help").Stdout, CliConsole.Run("--help").Stdout);
    }

    [Fact]
    public void NoLine_ExceedsEightyColumns()
    {
        foreach (var verb in new Verb?[] { null, Verb.New, Verb.Add, Verb.Pack, Verb.Version, Verb.Help })
        foreach (var line in Usage.Render(verb).Split('\n'))
            Assert.True(line.Length <= 80, $"{verb}: '{line}' is {line.Length} columns");
    }

    [Fact]
    public void Wrap_BreaksOnSpacesAndKeepsParagraphs()
    {
        Assert.Equal(["aaa bbb", "ccc", "", "ddd"], Usage.Wrap("aaa bbb ccc\n\nddd", 7).ToArray());
        Assert.Equal(["averyveryverylongword", "x"], Usage.Wrap("averyveryverylongword x", 5).ToArray());
    }
}

public class SuggestTests
{
    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("ab", "ba", 1)]
    [InlineData("same", "same", 0)]
    [InlineData("", "abc", 3)]
    public void Distance_IsDamerauLevenshtein(string a, string b, int expected) =>
        Assert.Equal(expected, Suggest.Distance(a, b));

    [Fact]
    public void Closest_PrefersExactPrefixesThenSmallEdits()
    {
        string[] verbs = ["new", "add", "convert", "pack", "version", "help"];
        Assert.Equal("convert", Suggest.Closest("conv", verbs));
        Assert.Equal("add", Suggest.Closest("ad", verbs));
        Assert.Null(Suggest.Closest("zzzz", verbs));
        Assert.Null(Suggest.Closest("xy", verbs));
    }
}

public class OptionalValueTokensTests
{
    [Fact]
    public void Normalize_AttachesFolderNamesAndMarksBareFlags()
    {
        Assert.Equal(["add", "--desktop=*", "--web=Site", "--tests=*", "./proj"],
            OptionalValueTokens.Normalize(["add", "--desktop", "--web", "Site", "--tests", "./proj"]));
    }

    [Fact]
    public void Normalize_LeavesAttachedFormsAndEverythingAfterDoubleDashAlone()
    {
        Assert.Equal(["add", "--desktop=Given", "--", "--web", "Folder"],
            OptionalValueTokens.Normalize(["add", "--desktop=Given", "--", "--web", "Folder"]));
    }

    [Theory]
    [InlineData("Folder", true)]
    [InlineData("My.Game.web2", true)]
    [InlineData(".", false)]
    [InlineData("./x", false)]
    [InlineData("a\\b", false)]
    [InlineData("-x", false)]
    public void IsFolderToken_RejectsPathLikeTokens(string token, bool expected) =>
        Assert.Equal(expected, OptionalValueTokens.IsFolderToken(token));
}
