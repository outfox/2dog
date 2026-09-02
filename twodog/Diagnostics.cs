using System.Xml;

namespace twodog.cli;

/// <summary>The process exit codes; scripts key off these, so they never shift.</summary>
internal static class ExitCodes
{
    public const int Ok = 0;

    /// <summary>Malformed command line.</summary>
    public const int Usage = 1;

    /// <summary>Project state, I/O, subprocess failure, partial apply, unexpected exception.</summary>
    public const int Error = 2;

    /// <summary>doctor: findings remain.</summary>
    public const int Findings = 3;

    /// <summary>Ctrl+C.</summary>
    public const int Cancelled = 130;
}

/// <summary>
/// One cancellation token for the run: Ctrl+C cancels prompts and kills subprocesses instead of tearing the process
/// down mid-write, so the terminal is restored and the exit code says "cancelled".
/// </summary>
internal static class Cancellation
{
    private static readonly CancellationTokenSource Source = new();

    public static CancellationToken Token => Source.Token;

    public static void Install() => Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Source.Cancel();
    };
}

/// <summary>Turns the exceptions a run can hit into an error line plus, where there is one, a hint.</summary>
internal static class FriendlyError
{
    public static (string Message, string? Hint) Describe(Exception ex) => ex switch
    {
        ToolException or UsageException => (ex.Message, null),
        UnauthorizedAccessException => (ex.Message,
            "check the permissions on the project directory and whether a file is read-only"),
        IOException io when IsInUse(io) => (io.Message,
            "the file is in use - close the Godot editor and any IDE holding the project, then re-run"),
        IOException io => (io.Message, null),
        XmlException xml => (
            $"{XmlFile(xml)} is not valid XML (line {xml.LineNumber}, column {xml.LinePosition}): {xml.Message}", null),
        HttpRequestException => ($"network request failed: {ex.Message}", "the nuget.org check is best effort; retry later"),
        _ => ($"unexpected {ex.GetType().Name}: {ex.Message}",
            "re-run with --verbose for a stack trace, and please report it at https://github.com/outfox/2dog/issues"),
    };

    private static string XmlFile(XmlException xml) =>
        Path.GetFileName(xml.SourceUri) is { Length: > 0 } file ? file : "a project file";

    /// <summary>Sharing violations on Windows, EBUSY/ETXTBSY elsewhere.</summary>
    private static bool IsInUse(IOException io) =>
        OperatingSystem.IsWindows()
            ? io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)
            : io.HResult is 16 or 26;
}
