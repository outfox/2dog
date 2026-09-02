using twodog.cli;

namespace twodog.tests.ToolTests;

// The subprocess runner behind `dotnet sln`, `dotnet restore` and doctor's probes: captured output, bounded
// waits, cancellation, and the muxer lookup.
public class ProcessRunnerTests
{
    private static ProcessRequest Dotnet(params string[] args) =>
        ProcessRunner.Dotnet(Path.GetTempPath(), null, TimeSpan.FromMinutes(2), args);

    [Fact]
    public void Run_CapturesOutput_AndLeaksNothingToTheConsole()
    {
        ProcessResult? result = null;
        var captured = CliConsole.Capture(() =>
        {
            result = ProcessRunner.Default.Run(Dotnet("--version"));
            return 0;
        });

        Assert.True(result!.Ok);
        Assert.Contains(result.Output, line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+\.\d+"));
        Assert.Equal("", captured.Stdout);
        Assert.Equal("", captured.Stderr);
        Assert.StartsWith("dotnet --version", result.CommandLine);
    }

    [Fact]
    public void Run_ReportsFailures_WithTheirTail()
    {
        ProcessResult? result = null;
        var captured = CliConsole.Capture(() =>
        {
            result = ProcessRunner.Default.Run(Dotnet("sln", "missing.slnx", "add", "nothing.csproj"));
            ProcessRunner.ReportFailure(result);
            return 0;
        });

        Assert.False(result!.Ok);
        Assert.NotEmpty(result.Output);
        Assert.Contains("error: 'dotnet sln missing.slnx add nothing.csproj' failed (exit", captured.Stderr);
    }

    [Fact]
    public void Verbose_EchoesEveryLine()
    {
        var captured = CliConsole.Capture(() =>
        {
            Out.Configure(OutputMode.Resolve(["-v"], _ => null, ConsoleFacts.Redirected));
            ProcessRunner.Default.Run(Dotnet("--version"));
            return 0;
        });

        Assert.Contains("verbose: run: dotnet --version", captured.Stderr);
        Assert.Matches(@"\|\s+\d+\.\d+", captured.Stderr);
    }

    [Fact]
    public void Run_TimesOut_AndKillsTheChild()
    {
        var request = ProcessRunner.Dotnet(Path.GetTempPath(), null, TimeSpan.FromMilliseconds(1), "--info");
        var result = ProcessRunner.Default.Run(request, TestContext.Current.CancellationToken);
        Assert.True(result.TimedOut);
        Assert.False(result.Ok);
        Assert.Equal("timed out", result.Outcome);
    }

    [Fact]
    public void Run_HonoursAPreCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProcessRunner.Default.Run(Dotnet("--info"), cts.Token));
    }

    // Ctrl+C mid-run kills the child and surfaces as cancellation, never as a timed-out or failed result.
    [Fact]
    public void Run_CancelledMidWay_ThrowsInsteadOfReportingATimeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        // A child that certainly outlives the cancel (dotnet --info finishes in ~100 ms on fast runners).
        var request = OperatingSystem.IsWindows()
            ? new ProcessRequest("ping", ["-n", "30", "127.0.0.1"], Path.GetTempPath(), TimeSpan.FromMinutes(2))
            : new ProcessRequest("sleep", ["30"], Path.GetTempPath(), TimeSpan.FromMinutes(2));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => ProcessRunner.Default.Run(request, cts.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), "the runner must not wait out the request timeout");
    }

    [Fact]
    public void Describe_QuotesArgumentsWithSpaces()
    {
        var request = new ProcessRequest("C:/x/dotnet.exe", ["sln", "dir with space/x.slnx", "add", ""], ".");
        Assert.Equal("dotnet sln \"dir with space/x.slnx\" add \"\"", ProcessRunner.Describe(request));
    }

    [Fact]
    public void Muxer_PrefersTheHostPath_ThenRoot_ThenPath()
    {
        string? Env(string name) => name switch
        {
            "DOTNET_HOST_PATH" => "/host/dotnet",
            "DOTNET_ROOT" => "/root",
            "PATH" => $"/nowhere{Path.PathSeparator}/bin",
            _ => null,
        };
        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        Assert.Equal("/host/dotnet", DotnetMuxer.Resolve(Env, p => p == "/host/dotnet"));
        Assert.Equal(Path.Combine("/root", exe), DotnetMuxer.Resolve(Env, p => p == Path.Combine("/root", exe)));
        Assert.Equal(Path.Combine("/bin", exe), DotnetMuxer.Resolve(Env, p => p == Path.Combine("/bin", exe)));
        var ex = Assert.Throws<ToolException>(() => DotnetMuxer.Resolve(Env, _ => false));
        Assert.Contains("DOTNET_HOST_PATH", ex.Message);
    }
}
