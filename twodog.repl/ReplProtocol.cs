using System.Text.Json;
using System.Text.Json.Serialization;

#if TWODOG_CLI
namespace twodog.cli.Repl;
#else
namespace twodog.Repl;
#endif

// One-shot requests on the REPL pipe, shared by the host and the 2dog tool (linked source).
// Keep this file free of Godot, Roslyn and PrettyPrompt types.
internal static class ReplProtocol
{
    internal const int Version = 1;
    internal const string ExecHello = "\u001e2dog.repl/exec/";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static T? Deserialize<T>(string line) => JsonSerializer.Deserialize<T>(line, Json);
}

internal static class ReplOps
{
    internal const string Exec = "exec", Sessions = "sessions", Close = "close";
}

internal static class ReplEvents
{
    // output: Console text; log: Godot messages and errors; result: one submission; done: the request.
    internal const string Output = "output", Log = "log", Result = "result", Sessions = "sessions", Done = "done";
}

internal static class ReplStatus
{
    internal const string Ok = "ok", Error = "error", Cancelled = "cancelled", Timeout = "timeout";
}

internal sealed record ReplRequest(string Op, string? Session = null, string? Code = null, double? Timeout = null);

internal sealed record ReplEvent(string Kind, string? Text = null, bool Error = false, string? Status = null,
    string? Input = null, string? Session = null, ReplSessionInfo[]? Sessions = null);

/// <summary>Scope and worlds are null while a submission blocks the engine thread.</summary>
internal sealed record ReplSessionInfo(string Name, string Mode, string? Scope, string[]? Worlds, bool Busy, double IdleSeconds);

/// <summary>A listening host, published so clients find it without knowing its pipe name.</summary>
internal sealed record ReplHostInfo(int Protocol, string Pipe, int Pid, DateTimeOffset Started, string Project, string[] Client);

internal static class ReplDiscovery
{
    /// <summary>One file per listening host. Tests point this at a private directory.</summary>
    internal static string Root { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2dog", "repl");

    internal static string PathOf(string pipe) => Path.Combine(Root, pipe + ".json");

    /// <summary>Pipe and session names: pipe names double as file names, so both stay portable.</summary>
    internal static bool IsValidName(string name)
        => name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && name[0] != '.';
}
