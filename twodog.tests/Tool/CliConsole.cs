using System.Text;
using twodog.cli;

namespace twodog.tests.ToolTests;

/// <summary>
/// Runs the in-process 2dog CLI with Console.Out/Error captured, so expected
/// usage and error output never leaks into the test log and can be asserted
/// on instead. The console is process-global state: while a capture is active,
/// writes from the capturing call chain (tracked via AsyncLocal) go to the
/// capture buffer and everything else - parallel test collections, engine
/// fixtures - passes through to the real console. Buffers are locked because a
/// StringWriter is not thread-safe and a concurrent write corrupts it.
/// </summary>
internal static class CliConsole
{
    private static readonly Lock Gate = new();
    private static readonly AsyncLocal<CaptureBuffers?> Current = new();
    private static readonly RoutingWriter RoutedOut = new(stdErr: false);
    private static readonly RoutingWriter RoutedError = new(stdErr: true);
    private static bool _installed;
    private static TextWriter? _realOut;
    private static TextWriter? _realError;

    public static (int ExitCode, string Stdout, string Stderr) Run(params string[] args) =>
        Capture(() => Program.Main(args));

    public static (int ExitCode, string Stdout, string Stderr) Capture(Func<int> action)
    {
        lock (Gate)
        {
            // Install the routers once (Console.SetOut wraps the writer, so
            // identity checks won't do); a plain swap would send concurrent
            // foreign output into the capture buffer.
            if (!_installed)
            {
                _installed = true;
                _realOut = Console.Out;
                _realError = Console.Error;
                Console.SetOut(RoutedOut);
                Console.SetError(RoutedError);
            }

            var capture = new CaptureBuffers();
            Current.Value = capture;
            try
            {
                return (action(), capture.Stdout.ToString(), capture.Stderr.ToString());
            }
            finally
            {
                Current.Value = null;
                // A run may have switched the output mode (--quiet, --json); the next test starts from the pinned default.
                Out.PinConsoleFacts(ConsoleFacts.Redirected);
            }
        }
    }

    private sealed class CaptureBuffers
    {
        public readonly StringBuilder Stdout = new();
        public readonly StringBuilder Stderr = new();
    }

    private sealed class RoutingWriter(bool stdErr) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        private TextWriter Passthrough => (stdErr ? _realError : _realOut) ?? TextWriter.Null;

        public override void Write(char value) => Write(value.ToString());

        public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));

        public override void Write(string? value)
        {
            if (value is null) return;
            if (Current.Value is { } capture)
            {
                var buffer = stdErr ? capture.Stderr : capture.Stdout;
                lock (buffer) buffer.Append(value);
            }
            else
            {
                Passthrough.Write(value);
            }
        }

        public override void Flush()
        {
            if (Current.Value is null) Passthrough.Flush();
        }
    }
}
