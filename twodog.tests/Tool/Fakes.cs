using System.Runtime.InteropServices;
using twodog.cli;

namespace twodog.tests.ToolTests;

/// <summary>A scripted process runner: answers by request, records what ran.</summary>
internal sealed class FakeProcessRunner(Func<ProcessRequest, ProcessResult> answer) : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public ProcessResult Run(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        lock (Requests) Requests.Add(request);
        return answer(request);
    }

    public static ProcessResult Result(ProcessRequest request, int exitCode, params string[] output) =>
        new(ProcessRunner.Describe(request), exitCode, output, TimeSpan.Zero, false);
}

/// <summary>A Windows x64 machine with nothing set, backed by the real file system for existence checks.</summary>
internal sealed class FakeEnvironment : IEnvironment
{
    public Dictionary<string, string> Vars { get; } = [];
    public string? Var(string name) => Vars.GetValueOrDefault(name);
    public bool IsWindows => true;
    public bool IsMacOS => false;
    public Architecture Architecture => Architecture.X64;
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
}
