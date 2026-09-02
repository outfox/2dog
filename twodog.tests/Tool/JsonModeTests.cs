using System.Text.Json;
using twodog.cli;

namespace twodog.tests.ToolTests;

// --json: exactly one document on stdout, nothing on stderr, also when the run fails.
public class JsonModeTests
{
    private static JsonElement Parse((int ExitCode, string Stdout, string Stderr) run)
    {
        Assert.Equal("", run.Stderr);
        return JsonDocument.Parse(run.Stdout).RootElement;
    }

    [Fact]
    public void DryRunAdd_DescribesTheProjectAndThePlan()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");

        var run = CliConsole.Run("add", tmp.Dir, "--desktop", "--dry-run", "--no-restore", "--json");
        Assert.Equal(0, run.ExitCode);
        var doc = Parse(run);

        Assert.Equal(1, doc.GetProperty("schema").GetInt32());
        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.Equal("add", doc.GetProperty("command").GetString());
        Assert.Equal("Game", doc.GetProperty("project").GetProperty("name").GetString());
        Assert.True(doc.GetProperty("dryRun").GetBoolean());
        Assert.Equal("desktop", doc.GetProperty("hosts")[0].GetProperty("kind").GetString());
        var actions = doc.GetProperty("actions").EnumerateArray().ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.Equal("planned", a.GetProperty("status").GetString()));
        Assert.Contains(actions, a => a.GetProperty("kind").GetString() == "createFile");
        Assert.Contains(actions, a => a.GetProperty("kind").GetString() == "solution");
    }

    [Fact]
    public void Json_NeverPrompts_AndAppliesTheDefaults()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");

        var run = CliConsole.Run("add", tmp.Dir, "--dry-run", "--no-restore", "--json");
        var doc = Parse(run);
        Assert.Equal(3, doc.GetProperty("hosts").GetArrayLength());
        Assert.Empty(doc.GetProperty("notes").EnumerateArray().Where(n => n.GetString()!.Contains("no terminal")));
    }

    [Fact]
    public void AppliedAdd_ReportsAppliedActionsAndNextSteps()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[application]\nconfig/name=\"Game\"\n");

        var run = CliConsole.Run("add", tmp.Dir, "--desktop", "--no-restore", "--json");
        Assert.Equal(0, run.ExitCode);
        var doc = Parse(run);
        Assert.All(doc.GetProperty("actions").EnumerateArray(), a => Assert.Equal("applied", a.GetProperty("status").GetString()));
        Assert.Contains("dotnet run --project", doc.GetProperty("nextSteps")[0].GetProperty("command").GetString());
        Assert.True(File.Exists(Path.Combine(tmp.Dir, "Game.2dog", "Game.2dog.csproj")));
    }

    [Fact]
    public void ToolError_StillProducesADocument()
    {
        var missing = Path.Combine(Path.GetTempPath(), "2dog-missing-" + Guid.NewGuid().ToString("N"));
        var run = CliConsole.Run("add", missing, "--desktop", "--json");
        Assert.Equal(ExitCodes.Error, run.ExitCode);
        var doc = Parse(run);
        Assert.False(doc.GetProperty("ok").GetBoolean());
        Assert.Equal(2, doc.GetProperty("exitCode").GetInt32());
        Assert.Contains("no project.godot", doc.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public void UsageError_StillProducesADocument()
    {
        var run = CliConsole.Run("add", "--dekstop", "--json");
        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        var doc = Parse(run);
        Assert.False(doc.GetProperty("ok").GetBoolean());
        Assert.Contains("unknown option '--dekstop'", doc.GetProperty("errors")[0].GetString());
        Assert.Contains("2dog add --help", doc.GetProperty("hints")[0].GetString());
    }

    [Fact]
    public void Version_ListsThePublishGroups()
    {
        var run = CliConsole.Run("version", "--json");
        Assert.Equal(0, run.ExitCode);
        var doc = Parse(run);
        var versions = doc.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(3, versions.Count);
        Assert.Equal("tool + packages", versions[0].GetProperty("label").GetString());
        Assert.Equal(ToolVersions.TwoDogVersion, versions[0].GetProperty("version").GetString());
    }

    [Fact]
    public void PackList_ListsEntries()
    {
        using var tmp = new TempProjectDir();
        var pck = tmp.Write("game.pck", PackToolTests.BuildPck(2, 0, ("res://scene.tscn", 40, 0), ("res://icon.png", 8, 0)));

        var run = CliConsole.Run("pack", "list", pck, "--json");
        Assert.Equal(0, run.ExitCode);
        var doc = Parse(run);
        var pack = doc.GetProperty("pack");
        Assert.Equal(2, pack.GetProperty("entries").GetArrayLength());
        Assert.Equal(48UL, pack.GetProperty("totalBytes").GetUInt64());
        Assert.Equal("res://scene.tscn", pack.GetProperty("entries")[0].GetProperty("path").GetString());
    }
}
