using System.Text;
using Godot;
using Godot.NativeInterop;

namespace twodog.Repl;

// Routes Console writes and Godot log messages raised by a submission to the request that ran it.
// Attribution follows the submission's execution context; other threads and frames keep the host console.
internal sealed class ReplCapture(Action<string, bool, bool> write)
{
    private readonly Action<string, bool, bool> write = write;
    private static readonly AsyncLocal<ReplCapture?> Current = new();
    private static readonly Lock RouterGate = new();
    private static TextWriter? routedOut, routedError;
    private volatile bool closed;

    /// <summary>Later writes, e.g. from a submission's background task, fall back to the host console.</summary>
    public void Close() => closed = true;

    internal static IDisposable Enter(ReplCapture? capture)
    {
        if (capture is not null) EnsureRouted();
        var previous = Current.Value;
        Current.Value = capture;
        return new Scope(previous);
    }

    private static bool TryWrite(string? text, bool error, bool log)
    {
        if (Current.Value is not { closed: false } capture) return false;
        if (!string.IsNullOrEmpty(text)) capture.write(text, error, log);
        return true;
    }

    // Console keeps an already synchronized writer as is, so identity checks see exactly this router.
    // Anyone may replace Console.Out later; the next capture routes the replacement instead.
    private static void EnsureRouted()
    {
        lock (RouterGate)
        {
            if (!ReferenceEquals(Console.Out, routedOut))
                Console.SetOut(routedOut = TextWriter.Synchronized(new Router(Console.Out, error: false)));
            if (!ReferenceEquals(Console.Error, routedError))
                Console.SetError(routedError = TextWriter.Synchronized(new Router(Console.Error, error: true)));
        }
    }

    private sealed class Scope(ReplCapture? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }

    private sealed class Router(TextWriter fallback, bool error) : TextWriter
    {
        public override Encoding Encoding => fallback.Encoding;
        public override void Write(char value)
        {
            if (!TryWrite(value.ToString(), error, log: false)) fallback.Write(value);
        }
        public override void Write(char[] buffer, int index, int count)
        {
            if (!TryWrite(new string(buffer, index, count), error, log: false)) fallback.Write(buffer, index, count);
        }
        public override void Write(string? value)
        {
            if (!TryWrite(value, error, log: false)) fallback.Write(value);
        }
        public override void WriteLine(string? value)
        {
            if (!TryWrite(value + NewLine, error, log: false)) fallback.WriteLine(value);
        }
        public override void Flush() => fallback.Flush();
    }

    // Godot calls loggers synchronously on the reporting thread, so a submission's prints and
    // errors see its execution context. Registered on the owner thread while a host serves requests.
    internal sealed class GodotLog : Logger
    {
        private static readonly StringName LogMessageMethod = "_LogMessage";
        private static readonly StringName LogErrorMethod = "_LogError";

        public override void _LogMessage(string message, bool error) => TryWrite(message, error, log: true);

        public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify,
            int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            var kind = (ErrorType)errorType switch
            {
                ErrorType.Warning => "WARNING",
                ErrorType.Script => "SCRIPT ERROR",
                ErrorType.Shader => "SHADER ERROR",
                _ => "ERROR",
            };
            TryWrite($"{kind}: {(rationale.Length > 0 ? rationale : code)}\n   at: {function} ({file}:{line})\n", error: true, log: true);
        }

        // Without Godot's source generators, declare the overrides that native virtual calls may dispatch.
        protected override bool HasGodotClassMethod(in godot_string_name method)
            => method == LogMessageMethod || method == LogErrorMethod || base.HasGodotClassMethod(method);
    }
}
