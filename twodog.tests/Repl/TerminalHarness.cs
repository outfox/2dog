using System.Text;
using System.Threading.Channels;
using twodog.Repl;

namespace twodog.tests.ReplTests;

/// <summary>Enough of an xterm-compatible terminal to check what the REPL screen shows: text with wide characters and
/// automatic wrapping, cursor movement, erasing, scrolling into scrollback and clearing.</summary>
internal sealed class VirtualTerminal(int width = 120, int height = 40) : ITerminalDevice
{
    private readonly Lock gate = new();
    private readonly StringBuilder raw = new();
    private readonly List<string> scrollback = [];
    private char[][] cells = Blank(width, height);
    private int row, column;
    private bool pendingWrap;
    private string escape = "";
    public int Width { get; private set; } = width;
    public int Height { get; private set; } = height;
    public (int Width, int Height) Size { get { lock (gate) return (Width, Height); } }
    public int Clears { get; private set; }
    public int Writes { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public (int Row, int Column) Cursor { get { lock (gate) return (row, column); } }
    public Action<string>? BeforeWrite { get; set; }
    public string Raw { get { lock (gate) return raw.ToString(); } }

    private static char[][] Blank(int width, int height) => Enumerable.Range(0, height).Select(_ => Enumerable.Repeat(' ', width).ToArray()).ToArray();

    /// <summary>Changes the size without reflowing, like a terminal that truncates rows.</summary>
    public void Resize(int newWidth, int newHeight)
    {
        lock (gate)
        {
            var resized = Blank(newWidth, newHeight);
            var skip = Math.Max(0, row + 1 - newHeight);
            for (var r = 0; r < skip; r++) scrollback.Add(Line(cells[r]));
            for (var r = skip; r < Math.Min(Height, skip + newHeight); r++)
                Array.Copy(cells[r], resized[r - skip], Math.Min(Width, newWidth));
            cells = resized;
            row -= skip;
            column = Math.Min(column, newWidth - 1);
            (Width, Height) = (newWidth, newHeight);
        }
    }

    public string[] Screen { get { lock (gate) return cells.Select(Line).ToArray(); } }
    public string[] Scrollback { get { lock (gate) return scrollback.ToArray(); } }
    /// <summary>Scrollback and screen, without trailing blank rows.</summary>
    public string Text
    {
        get
        {
            lock (gate)
            {
                var lines = scrollback.Concat(cells.Select(Line)).ToList();
                while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                return string.Join("\n", lines);
            }
        }
    }

    private static string Line(char[] line) => new string(line.Where(c => c != '\0').ToArray()).TrimEnd();

    public void Write(string ansi)
    {
        BeforeWrite?.Invoke(ansi);
        lock (gate)
        {
            Writes++;
            raw.Append(ansi);
            foreach (var rune in ansi.EnumerateRunes()) Feed(rune);
        }
    }

    private void Feed(Rune rune)
    {
        var c = rune.Value;
        if (escape.Length > 0)
        {
            escape += rune.ToString();
            if (escape.StartsWith("\x1b]"))
            {
                if (c == 7 || escape.EndsWith("\x1b\\")) escape = "";
                return;
            }
            if (escape.Length == 2 && c != '[') { escape = ""; return; }
            if (escape.Length > 2 && c is >= '@' and <= '~')
            {
                Csi(escape[2..^1], (char)c);
                escape = "";
            }
            return;
        }
        switch (c)
        {
            case 0x1b: escape = "\x1b"; return;
            case '\r': column = 0; pendingWrap = false; return;
            case '\n': LineFeed(); pendingWrap = false; return;
            case '\b': column = Math.Max(0, column - 1); pendingWrap = false; return;
            case '\t': column = Math.Min(Width - 1, (column / 8 + 1) * 8); return;
            case < 0x20: return;
        }
        var cellWidth = CellWidth.Of(rune);
        if (cellWidth == 0) return;
        if (pendingWrap || column + cellWidth > Width)
        {
            column = 0;
            LineFeed();
            pendingWrap = false;
        }
        var text = rune.ToString();
        cells[row][column] = text[0];
        if (cellWidth == 2 && column + 1 < Width) cells[row][column + 1] = '\0';
        column += cellWidth;
        if (column >= Width)
        {
            column = Width - 1;
            pendingWrap = true;
        }
    }

    private void LineFeed()
    {
        if (row < Height - 1)
        {
            row++;
            return;
        }
        scrollback.Add(Line(cells[0]));
        Array.Copy(cells, 1, cells, 0, Height - 1);
        cells[Height - 1] = Enumerable.Repeat(' ', Width).ToArray();
    }

    private void Csi(string parameters, char final)
    {
        var isPrivate = parameters.StartsWith('?') || parameters.StartsWith('>') || parameters.StartsWith('<');
        var values = (isPrivate ? parameters[1..] : parameters).Split(';').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var n = Math.Max(1, values[0]);
        switch (final)
        {
            case 'A' when !isPrivate: row = Math.Max(0, row - n); pendingWrap = false; break;
            case 'B' when !isPrivate: row = Math.Min(Height - 1, row + n); pendingWrap = false; break;
            case 'C' when !isPrivate: column = Math.Min(Width - 1, column + n); pendingWrap = false; break;
            case 'D' when !isPrivate: column = Math.Max(0, column - n); pendingWrap = false; break;
            case 'H':
                row = Math.Clamp((values.Length > 0 ? Math.Max(1, values[0]) : 1) - 1, 0, Height - 1);
                column = Math.Clamp((values.Length > 1 ? Math.Max(1, values[1]) : 1) - 1, 0, Width - 1);
                pendingWrap = false;
                break;
            case 'K':
                for (var x = column; x < Width; x++) cells[row][x] = ' ';
                break;
            case 'J':
                if (values[0] == 0)
                {
                    for (var x = column; x < Width; x++) cells[row][x] = ' ';
                    for (var y = row + 1; y < Height; y++) cells[y] = Enumerable.Repeat(' ', Width).ToArray();
                }
                else if (values[0] == 2)
                {
                    cells = Blank(Width, Height);
                    Clears++;
                }
                else if (values[0] == 3) scrollback.Clear();
                break;
            case 'h' or 'l' when parameters == "?25":
                CursorVisible = final == 'h';
                break;
        }
    }
}

/// <summary>A scripted interactive terminal: keys and pastes in, a virtual terminal out. It serves as a host terminal
/// (drawing the REPL screen) or as a connected client (showing frames rendered by a host).</summary>
internal sealed class ScriptedTerminal : IReplTerminal, IReplClientTerminal, IDisposable
{
    private readonly Channel<TerminalInput> inputs = Channel.CreateUnbounded<TerminalInput>();
    private readonly CancellationToken? waitForInput;
    private readonly bool interactive;
    private ReplScreen? screen;
    public VirtualTerminal Device { get; } = new();
    ITerminalDevice IReplClientTerminal.Device => Device;
    public StringWriter Stdout { get; } = new();
    public StringWriter Stderr { get; } = new();
    public TextReader Input { get; }
    public ReplOutput Output { get; }
    public bool Interactive => interactive;
    public bool Color { get; set; } = true;
    public bool Restored { get; private set; }
    public string HistoryFile { get; } = Path.Combine(Path.GetTempPath(), "2dog-repl-" + Guid.NewGuid(), "history");
    /// <summary>Runs before each input is handed to the editor.</summary>
    public Action? BeforeRead { get; set; }
    public event Action? Interrupted;
    public Action? Cancel { get; private set; }
    public ReplScreen? Screen => interactive ? screen ??= new ReplScreen(Device, Color) : null;

    /// <param name="input">Redirected input, or null for an interactive terminal.</param>
    /// <param name="waitForInput">Wait for more keys until cancelled instead of failing when they run out.</param>
    public ScriptedTerminal(string? input, IEnumerable<ConsoleKeyInfo> keys, CancellationToken? waitForInput = null, TextReader? reader = null)
    {
        interactive = input is null;
        Input = reader ?? new StringReader(input ?? "");
        this.waitForInput = waitForInput;
        foreach (var key in keys) Press(key);
        Output = new ReplOutput(new Tee(Stdout, () => Screen, false), new Tee(Stderr, () => Screen, true), color: false, errorColor: false);
    }

    public void Press(ConsoleKeyInfo key) => inputs.Writer.TryWrite(new KeyInput(key));
    public void Paste(string text) => inputs.Writer.TryWrite(new TextInput(text));
    public void Interrupt() => Interrupted?.Invoke();

    public async Task<TerminalInput> ReadAsync(CancellationToken token)
    {
        BeforeRead?.Invoke();
        if (inputs.Reader.TryRead(out var input)) return input;
        if (waitForInput is not { } wait) throw new InvalidOperationException("The editor did not submit after the expected keys.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, wait);
        return await inputs.Reader.ReadAsync(linked.Token);
    }

    public void SetCancellationHandler(Action? cancel) => Cancel = cancel;

    public void Restore()
    {
        screen?.Dispose();
        Restored = true;
    }

    public void Dispose()
    {
        screen?.Dispose();
        Input.Dispose();
        Stdout.Dispose();
        Stderr.Dispose();
        var directory = Path.GetDirectoryName(HistoryFile)!;
        // The test creates only this unique history file and directory.
        if (File.Exists(HistoryFile)) File.Delete(HistoryFile);
        if (Directory.Exists(directory)) Directory.Delete(directory);
    }

    // Keeps a plain transcript for assertions while an interactive terminal also draws the output.
    private sealed class Tee(StringWriter transcript, Func<ReplScreen?> screen, bool error) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Write(value.ToString());
        public override void Write(string? value)
        {
            lock (transcript) transcript.Write(value);
            screen()?.Write(value, error);
        }
        public override void WriteLine(string? value) => Write(value + NewLine);
    }
}

internal static class Keys
{
    public static readonly ConsoleKeyInfo Enter = new('\r', ConsoleKey.Enter, false, false, false);
    public static readonly ConsoleKeyInfo ShiftEnter = new('\r', ConsoleKey.Enter, true, false, false);
    public static readonly ConsoleKeyInfo ControlEnter = new('\r', ConsoleKey.Enter, false, false, true);
    public static readonly ConsoleKeyInfo AltEnter = new('\r', ConsoleKey.Enter, false, true, false);
    public static readonly ConsoleKeyInfo Tab = new('\t', ConsoleKey.Tab, false, false, false);
    public static readonly ConsoleKeyInfo ShiftTab = new('\t', ConsoleKey.Tab, true, false, false);
    public static readonly ConsoleKeyInfo Escape = new('\x1b', ConsoleKey.Escape, false, false, false);
    public static readonly ConsoleKeyInfo Backspace = new('\b', ConsoleKey.Backspace, false, false, false);
    public static readonly ConsoleKeyInfo ShiftBackspace = new('\b', ConsoleKey.Backspace, true, false, false);
    public static readonly ConsoleKeyInfo Left = new('\0', ConsoleKey.LeftArrow, false, false, false);
    public static readonly ConsoleKeyInfo Right = new('\0', ConsoleKey.RightArrow, false, false, false);
    public static readonly ConsoleKeyInfo Up = new('\0', ConsoleKey.UpArrow, false, false, false);
    public static readonly ConsoleKeyInfo Down = new('\0', ConsoleKey.DownArrow, false, false, false);
    public static readonly ConsoleKeyInfo ControlC = new('\x03', ConsoleKey.C, false, false, true);
    public static readonly ConsoleKeyInfo ControlD = new('\x04', ConsoleKey.D, false, false, true);
    public static readonly ConsoleKeyInfo ControlSpace = new(' ', ConsoleKey.Spacebar, false, false, true);
    public static ConsoleKeyInfo Control(ConsoleKey key, bool shift = false) => new((char)(key - ConsoleKey.A + 1), key, shift, false, true);

    public static IEnumerable<ConsoleKeyInfo> Type(string text) => text.Select(c => new ConsoleKeyInfo(c, c switch
    {
        '\n' => ConsoleKey.Enter,
        '$' => ConsoleKey.D4, '.' => ConsoleKey.OemPeriod, '/' => ConsoleKey.Oem2, '(' => ConsoleKey.D9, ')' => ConsoleKey.D0,
        '"' => ConsoleKey.Oem7, '[' => ConsoleKey.Oem4, ']' => ConsoleKey.Oem6, ' ' => ConsoleKey.Spacebar,
        >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' => (ConsoleKey)char.ToUpperInvariant(c),
        _ => ConsoleKey.NoName,
    }, char.IsUpper(c) || c is '$' or '(' or ')' or '"' or '\n', false, false));
}

/// <summary>Feeds keys to an editor until it submits or cancels.</summary>
internal static class EditorDriver
{
    public static async Task<EditorResult> RunAsync(ReplEditor editor, IEnumerable<ConsoleKeyInfo> keys, CancellationToken token = default)
    {
        using var queue = keys.GetEnumerator();
        return await editor.ReadLineAsync(_ => queue.MoveNext()
            ? Task.FromResult<TerminalInput>(new KeyInput(queue.Current))
            : throw new InvalidOperationException("The editor did not submit after the expected keys."), token);
    }

    public static async Task<string> ReadAsync(IReplLanguage language, IEnumerable<ConsoleKeyInfo> keys, bool multiline = false, CancellationToken token = default)
        => (await RunAsync(new ReplEditor(language) { Multiline = multiline }, keys, token)).Text;

    /// <summary>All keys are queued before the editor reads, as when typed ahead during startup.</summary>
    public static async Task<string> ReadQueuedAsync(IReplLanguage language, IEnumerable<ConsoleKeyInfo> keys, bool windows = true, bool multiline = false)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        var reader = new ReplKeyReader(() => queue.Count > 0, queue.Dequeue, windows);
        var editor = new ReplEditor(language) { Multiline = multiline };
        return (await editor.ReadLineAsync(_ => queue.Count > 0 || reader.Available
            ? Task.FromResult(reader.Read(idle: false))
            : throw new InvalidOperationException("The editor did not submit after the expected keys."), CancellationToken.None)).Text;
    }
}
