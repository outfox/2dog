using PrettyPrompt.Consoles;
using twodog.Repl;

namespace twodog.tests.ReplTests;

// Feed individual key events through the real prompt without touching the user's console.
internal sealed class PromptConsole : IConsole
{
    private readonly Queue<ConsoleKeyInfo> keys;
    private readonly ReplInputBuffer? input;
    private readonly CancellationToken? waitForInput;
    public int Clears { get; private set; }
    public ReplPromptOutput? Output { get; set; }
    public Action? BeforeRead { get; set; }
    public bool NewlineAutoReturn { get; private set; } = true;
    public List<string> Writes { get; } = [];
    public List<string> Operations { get; } = [];

    public PromptConsole(IEnumerable<ConsoleKeyInfo> keys, bool buffered = false, bool windows = true, CancellationToken? waitForInput = null)
    {
        this.keys = new(keys);
        this.waitForInput = waitForInput;
        if (buffered) input = new ReplInputBuffer(() => this.keys.Count > 0, ReadNext, windows);
    }
    public int CursorTop => 0;
    public int BufferWidth => 120;
    public int WindowHeight => 40;
    public int WindowTop => 0;
    public bool KeyAvailable => Output?.Restoring != true && (input?.KeyAvailable ?? false);
    public bool IsErrorRedirected => false;
    public bool CaptureControlC { get; set; }
    public event ConsoleCancelEventHandler? CancelKeyPress;
    public bool CancelExecution()
    {
        var args = (ConsoleCancelEventArgs)Activator.CreateInstance(typeof(ConsoleCancelEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, args: [ConsoleSpecialKey.ControlC], culture: null)!;
        CancelKeyPress?.Invoke(this, args);
        return args.Cancel;
    }
    public string? TakePaste() => input?.TakePaste();
    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        BeforeRead?.Invoke();
        return Output?.ReadRestoreKey() ?? input?.ReadKey() ?? ReadNext();
    }
    private ConsoleKeyInfo ReadNext()
    {
        if (keys.TryDequeue(out var key)) return key;
        if (waitForInput is { } token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Output?.CheckForOutput();
                Thread.Sleep(1);
            }
        }
        throw new InvalidOperationException("Prompt did not submit after the expected key events.");
    }
    public void Write(string? value) => Writes.Add(value ?? "");
    public void WriteLine(string? value) { }
    public void WriteError(string? value) { }
    public void WriteErrorLine(string? value) { }
    public void Write(ReadOnlySpan<char> value) => Writes.Add(value.ToString());
    public void WriteLine(ReadOnlySpan<char> value) { }
    public void WriteError(ReadOnlySpan<char> value) { }
    public void WriteErrorLine(ReadOnlySpan<char> value) { }
    public void Clear() => Clears++;
    public void ShowCursor() => Operations.Add("show");
    public void HideCursor() => Operations.Add("hide");
    public void InitVirtualTerminalProcessing() => Operations.Add("init");
    public void SetNewlineAutoReturn(bool enabled) => NewlineAutoReturn = enabled;
    public void SetModifyOtherKeys(bool enabled) => Operations.Add("modify:" + enabled);
}
