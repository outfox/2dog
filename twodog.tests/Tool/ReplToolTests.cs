using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using twodog.cli;
using twodog.cli.Repl;
using twodog.tests.EngineTests;
using twodog.tests.ReplTests;

namespace twodog.tests.ToolTests;

public class ReplCommandLineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParsingUsesItsOwnJsonFlagInsteadOfTheActiveOutputMode(bool activeJson)
    {
        CliConsole.Capture(() =>
        {
            Out.Configure(OutputMode.Resolve(activeJson ? ["--json"] : [], _ => null, ConsoleFacts.Redirected));
            Assert.Equal(new ReplOptions(ReplOp.Attach, null), CommandLine.Parse(["repl"]).Repl);
            var error = Assert.Throws<UsageException>(() => CommandLine.Parse(["--json", "repl"]));
            Assert.Contains("use '2dog repl exec' for --json", error.Message);
            Assert.Equal(new ReplOptions(ReplOp.Exec, null, Code: "1"),
                CommandLine.Parse(["--json", "repl", "exec", "1"]).Repl);
            return 0;
        });
    }

    [Fact]
    public void OperationsParseIntoTheirRequests()
    {
        Assert.Equal(new ReplOptions(ReplOp.Attach, null), CommandLine.Parse(["repl"]).Repl);
        Assert.Equal(new ReplOptions(ReplOp.Sessions, "game"), CommandLine.Parse(["repl", "--pipe", "game", "sessions"]).Repl);
        Assert.Equal(new ReplOptions(ReplOp.Close, null, "agent"), CommandLine.Parse(["repl", "close", "agent"]).Repl);
        Assert.Equal(new ReplOptions(ReplOp.Exec, "game", "agent", 2.5, "ls $Player"),
            CommandLine.Parse(["repl", "exec", "-s", "agent", "--timeout", "2.5", "--pipe", "game", "ls", "$Player"]).Repl);
        Assert.Null(CommandLine.Parse(["repl", "exec", "-"]).Repl!.Code);
        Assert.Null(CommandLine.Parse(["repl", "exec"]).Repl!.Code);
        Assert.Equal("-1", CommandLine.Parse(["repl", "exec", "--", "-1"]).Repl!.Code);
    }

    [Theory]
    [InlineData("unknown repl operation 'ls'", "repl", "ls")]
    [InlineData("2dog repl close needs a session name", "repl", "close")]
    [InlineData("2dog repl close takes one session name", "repl", "close", "a", "b")]
    [InlineData("2dog repl sessions takes no arguments", "repl", "sessions", "a")]
    [InlineData("'no/slash' is not a REPL pipe name", "repl", "--pipe", "no/slash")]
    [InlineData("'two words' is not a session name", "repl", "exec", "-s", "two words", "1")]
    [InlineData("--timeout takes a positive number of seconds, not 'soon'", "repl", "exec", "--timeout", "soon", "1")]
    [InlineData("--timeout takes a positive number of seconds, not '0'", "repl", "exec", "--timeout", "0", "1")]
    [InlineData("'--session' is not an option of '2dog repl'", "repl", "--session", "a")]
    public void MalformedCommandLinesAreUsageErrors(string message, params string[] args)
    {
        var error = Assert.Throws<UsageException>(() => CommandLine.Parse(args));
        Assert.Contains(message, error.Message);
        Assert.Equal(Verb.Repl, error.Verb);
    }

    [Fact]
    public void AttachingNeedsATerminalAndHelpListsTheOperations()
    {
        var (exit, stdout, _) = CliConsole.Run("--json", "repl");
        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("use '2dog repl exec' for --json", JsonDocument.Parse(stdout).RootElement.GetProperty("errors")[0].GetString());
        var help = Usage.Render(Verb.Repl);
        Assert.Contains("usage: 2dog repl [operation] [options]", help);
        Assert.Contains("repl exec [code]", help);
        Assert.Contains("--timeout <seconds>", help);
        var general = Usage.Render(null);
        Assert.Contains("  repl  ", general);
        Assert.Contains("repl close <session>", general);
    }
}

public class ReplHostLookupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "2dog-repl-lookup-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Started = new(Process.GetCurrentProcess().StartTime);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private ReplHostInfo Publish(string pipe, string project, int? pid = null, DateTimeOffset? started = null, int protocol = ReplProtocol.Version)
    {
        var host = new ReplHostInfo(protocol, pipe, pid ?? Environment.ProcessId, started ?? Started, project, ["client"]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, pipe + ".json"), ReplProtocol.Serialize(host));
        return host;
    }

    [Fact]
    public void OnlyFilesOfRunningHostsCountAndStaleOnesAreRemoved()
    {
        Assert.Empty(ReplHosts.Running(root));
        var live = Publish("live", "/games/a");
        Publish("restarted", "/games/a", started: Started.AddMinutes(-5));
        using (var exited = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true })!)
        {
            exited.WaitForExit();
            Publish("exited", "/games/a", pid: exited.Id);
        }
        File.WriteAllText(Path.Combine(root, "torn.json"), "{\"protocol\":");
        File.WriteAllText(Path.Combine(root, "nameless.json"), "{\"protocol\":1}");
        File.WriteAllText(Path.Combine(root, "null.json"), "null");

        Assert.Equal([live.Pipe], ReplHosts.Running(root).Select(h => h.Pipe));
        Assert.Equal(new[] { "live.json", "nameless.json", "null.json", "torn.json" }, Directory.GetFiles(root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void TheProjectDirectoryChoosesAmongHostsAndAmbiguityIsAnError()
    {
        var games = Path.Combine(root, "games");
        Assert.Contains("no REPL host is running", Assert.Throws<ToolException>(() => ReplHosts.Find(null, games, root)).Message);
        Publish("a1", Path.Combine(games, "a") + "/");
        Publish("b", Path.Combine(games, "b"));
        Assert.Equal("a1", ReplHosts.Find(null, Path.Combine(games, "a", "nested"), root).Pipe);
        Assert.Equal("b", ReplHosts.Find("b", Path.Combine(games, "a"), root).Pipe);
        var unknown = Assert.Throws<ToolException>(() => ReplHosts.Find("c", games, root)).Message;
        Assert.Contains("no running REPL host listens on 'c'; running hosts: ", unknown);
        Assert.Contains("2 REPL hosts are running - choose one with --pipe", Assert.Throws<ToolException>(() => ReplHosts.Find(null, games, root)).Message);
        Publish("a2", Path.Combine(games, "a"));
        Assert.Contains("2 REPL hosts are running for this project", Assert.Throws<ToolException>(() => ReplHosts.Find(null, Path.Combine(games, "a"), root)).Message);
        Publish("old", Path.Combine(games, "old"), protocol: 0);
        Assert.Contains("speaks protocol 0, this tool 1", Assert.Throws<ToolException>(() => ReplHosts.Find("old", games, root)).Message);
    }

    [Fact]
    public void TheOnlyRunningHostServesOtherDirectoriesWithANote()
    {
        Publish("only", Path.Combine(root, "game"));
        var (exit, _, stderr) = CliConsole.Capture(() => ReplHosts.Find(null, Path.GetTempPath(), root).Pipe == "only" ? 0 : 1);
        Assert.Equal(0, exit);
        Assert.Contains("note: using the only running REPL host, 'only'", stderr);
    }

    [Theory]
    [InlineData("/games/a", "/games/a", true)]
    [InlineData("/games/a/", "/games/a/levels", true)]
    [InlineData("/games/a", "/games/ab", false)]
    [InlineData("/games/a", "/games", false)]
    [InlineData("", "/games", false)]
    public void ProjectsContainTheirOwnSubdirectoriesOnly(string project, string directory, bool expected)
        => Assert.Equal(expected, ReplHosts.Contains(project, directory));
}

// The full verb against a live host, in process: the engine is pumped here while the tool runs on the pool.
[Collection(nameof(EngineRestartCollection))]
public class ReplToolTests
{
    private static (int ExitCode, string Stdout, string Stderr) Tool(LiveHost live, params string[] args)
    {
        var run = Task.Run(() => CliConsole.Run(args));
        live.Pump(run);
        return run.Result;
    }

    private static void Publish(LiveHost live, string[] client)
    {
        Directory.CreateDirectory(ReplDiscovery.Root);
        File.WriteAllText(ReplDiscovery.PathOf(live.Pipe), ReplProtocol.Serialize(new ReplHostInfo(ReplProtocol.Version, live.Pipe,
            Environment.ProcessId, new DateTimeOffset(Process.GetCurrentProcess().StartTime), Engine.ResolveProjectDir(), client)));
    }

    [Fact]
    public void ExecReportsValuesErrorsAndLogsWithDistinctExitCodes()
    {
        using var live = new LiveHost("repl-tool-exec");
        Publish(live, []);
        try
        {
            var created = Tool(live, "repl", "exec", "--pipe", live.Pipe, "-s", "tool", "var v = 6 * 7;", "GD.Print(\"printed\");", "v");
            Assert.Equal((0, "printed\n42" + Environment.NewLine, ""), created);

            var failed = Tool(live, "repl", "exec", "--pipe", live.Pipe, "-s", "tool", "GD.PushError(\"bad\"); missing");
            Assert.Equal(ExitCodes.SubmissionFailed, failed.ExitCode);
            Assert.Contains("CS0103", failed.Stderr);
            Assert.Equal("", failed.Stdout);
            Assert.Empty(live.Engine.Errors.Drain());

            var logged = Tool(live, "repl", "exec", "--pipe", live.Pipe, "-s", "tool", "GD.PushWarning(\"careful\"); v");
            Assert.Equal((0, "42" + Environment.NewLine), (logged.ExitCode, logged.Stdout));
            Assert.Contains("WARNING: careful", logged.Stderr);
            Assert.Single(live.Engine.Errors.Drain());

            var timedOut = Tool(live, "repl", "exec", "--pipe", live.Pipe, "-s", "tool", "--timeout", "0.3", "while (true) { }");
            Assert.Equal(ExitCodes.TimedOut, timedOut.ExitCode);
            Assert.Contains("error: timed out after 0.3 s", timedOut.Stderr);

            var json = Tool(live, "repl", "exec", "--json", "--pipe", live.Pipe, "-s", "tool", "Console.Write(\"out\"); GD.Print(\"log\"); v\nnope");
            Assert.Equal(ExitCodes.SubmissionFailed, json.ExitCode);
            var repl = JsonDocument.Parse(json.Stdout).RootElement.GetProperty("repl");
            Assert.Equal((live.Pipe, "tool", "error"), (repl.GetProperty("pipe").GetString(), repl.GetProperty("session").GetString(), repl.GetProperty("status").GetString()));
            var results = repl.GetProperty("results");
            Assert.Equal(("ok", "42", "out", "log\n"), (results[0].GetProperty("status").GetString(), results[0].GetProperty("value").GetString(),
                results[0].GetProperty("output").GetString(), results[0].GetProperty("log").GetString()));
            Assert.Contains("CS0103", results[1].GetProperty("error").GetString());
            Assert.Equal("", json.Stderr);

            var original = Console.In;
            try
            {
                Console.SetIn(new StringReader("var piped = 1;\npiped + v\n"));
                var piped = Tool(live, "repl", "exec", "--pipe", live.Pipe, "-s", "tool");
                Assert.Equal((0, "43" + Environment.NewLine), (piped.ExitCode, piped.Stdout));
            }
            finally { Console.SetIn(original); }
        }
        finally { File.Delete(ReplDiscovery.PathOf(live.Pipe)); }
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }

    [Fact]
    public void SessionsCloseAndAttachTalkToTheProjectsHost()
    {
        using var live = new LiveHost("repl-tool-sessions");
        Publish(live, ["host-client", "client.dll"]);
        var launch = ReplCommand.Launch;
        try
        {
            var none = Tool(live, "repl", "sessions");
            Assert.Equal(0, none.ExitCode);
            Assert.Contains("no named sessions in REPL host", none.Stdout);

            Assert.Equal(0, Tool(live, "repl", "exec", "-s", "first", "cd $Control\nworld.Clear();").ExitCode);
            Assert.Equal(0, Tool(live, "repl", "exec", "-s", "second", ":cs").ExitCode);
            var listed = Tool(live, "repl", "sessions");
            var rows = listed.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => Regex.Split(r.Trim(), @"\s{2,}")).ToList();
            Assert.Equal(["first", ":auto", "/root/Control", "default"], rows[0][..4]);
            Assert.Equal(["second", ":cs", "/root", "-"], rows[1][..4]);
            Assert.StartsWith("idle ", rows[0][4]);
            var json = JsonDocument.Parse(Tool(live, "repl", "sessions", "--json").Stdout).RootElement.GetProperty("repl").GetProperty("sessions");
            Assert.Equal(2, json.GetArrayLength());

            var closed = Tool(live, "repl", "close", "first");
            Assert.Equal((0, "closed session 'first'" + Environment.NewLine), (closed.ExitCode, closed.Stdout));
            var missing = Tool(live, "repl", "close", "first");
            Assert.Equal(ExitCodes.Error, missing.ExitCode);
            Assert.Contains("error: no session named 'first'", missing.Stderr);

            ProcessStartInfo? started = null;
            ReplCommand.Launch = info => { started = info; return 7; };
            Assert.Equal(7, Tool(live, "repl").ExitCode);
            Assert.Equal("host-client", started!.FileName);
            Assert.Equal(["client.dll", "--repl-connect", live.Pipe], started.ArgumentList);
            ReplCommand.Launch = _ => throw new Win32Exception(2, "not found");
            Assert.Contains("could not start the REPL client host-client: not found", Tool(live, "repl").Stderr);
            Publish(live, []);
            var clientless = Tool(live, "repl");
            Assert.Equal(ExitCodes.Error, clientless.ExitCode);
            Assert.Contains($"run its executable with --repl-connect {live.Pipe}", clientless.Stderr);
        }
        finally
        {
            ReplCommand.Launch = launch;
            File.Delete(ReplDiscovery.PathOf(live.Pipe));
        }
        live.Server.Dispose();
        Assert.Empty(live.Engine.Errors.Drain());
    }
}
