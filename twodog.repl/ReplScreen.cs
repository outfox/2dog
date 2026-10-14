using System.Text;
using System.Text.RegularExpressions;

namespace twodog.Repl;

/// <summary>Where the screen draws: a real console, a connected client, or a test.</summary>
internal interface ITerminalDevice
{
    (int Width, int Height) Size { get; }
    void Write(string ansi);
}

/// <summary>The only writer to an interactive terminal. Finished output lines are committed above a live input region,
/// which is redrawn below them, so output from any thread or process never lands inside the draft being typed.</summary>
internal sealed class ReplScreen : IDisposable
{
    private const string BeginFrame = "\x1b[?2026h\x1b[?25l", EndFrame = "\x1b[?2026l";
    private readonly ITerminalDevice device;
    private readonly bool color;
    private readonly Lock gate = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly Thread thread;
    private readonly List<string> committed = [];
    private readonly StringBuilder[] partial = [new(), new()];
    private long partialSince;
    private Func<int, int, RegionFrame>? region;
    private bool dirty, clear, stopping;
    private RegionFrame? drawn;
    private (int Width, int Height) drawnSize;
    private Exception? failure;

    public ReplScreen(ITerminalDevice device, bool color)
    {
        this.device = device;
        this.color = color;
        thread = new Thread(Run) { IsBackground = true, Name = "2dog REPL screen" };
        thread.Start();
    }

    public bool Color => color;
    public (int Width, int Height) Size => device.Size;
    /// <summary>While this returns true the region redraws several times a second, e.g. for a progress indicator.</summary>
    public Func<bool>? Animating { get; set; }
    private bool animated;

    /// <summary>A control sequence for the terminal itself, such as a clipboard request; it is not output text.</summary>
    public void Control(string sequence)
    {
        lock (gate) controls.Append(sequence);
        signal.Release();
    }

    /// <summary>Output text, possibly partial lines; written above the region once each line is complete or idle.</summary>
    public void Write(string? text, bool error = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (gate)
        {
            var pending = partial[error ? 1 : 0];
            pending.Append(text);
            var value = pending.ToString();
            var end = value.LastIndexOf('\n');
            if (end >= 0)
            {
                foreach (var line in value[..end].Split('\n')) committed.Add(Clean(line));
                pending.Clear().Append(value[(end + 1)..]);
            }
            if (pending.Length > 0 && partialSince == 0) partialSince = Environment.TickCount64;
        }
        signal.Release();
    }

    /// <summary>Sets the live region; the delegate receives the terminal size and runs on the render thread.</summary>
    public void SetRegion(Func<int, int, RegionFrame>? layout)
    {
        lock (gate)
        {
            region = layout;
            dirty = true;
        }
        signal.Release();
    }

    /// <summary>Redraws the region, e.g. after its content or the terminal size changed.</summary>
    public void Invalidate()
    {
        lock (gate) dirty = true;
        signal.Release();
    }

    public void Clear()
    {
        lock (gate) clear = dirty = true;
        signal.Release();
    }

    /// <summary>Waits until everything written so far is on the terminal.</summary>
    public void Flush()
    {
        using var done = new ManualResetEventSlim();
        lock (gate)
        {
            flushes.Add(done);
            FlushPartial(force: true);
        }
        signal.Release();
        while (!done.Wait(50) && thread.IsAlive) { }
        if (failure is { } error) throw new IOException("The terminal stopped accepting output.", error);
    }

    private readonly List<ManualResetEventSlim> flushes = [];
    private readonly StringBuilder controls = new();

    // Under the gate: an idle partial line (a prompt or progress text) is written as a line of its own.
    private void FlushPartial(bool force)
    {
        if (partialSince == 0 || !force && Environment.TickCount64 - partialSince < 100) return;
        foreach (var pending in partial)
        {
            if (pending.Length > 0) committed.Add(Clean(pending.ToString()));
            pending.Clear();
        }
        partialSince = 0;
    }

    private void Run()
    {
        while (true)
        {
            var animating = Animating?.Invoke() == true;
            signal.Wait(animating || partialSince != 0 ? 100 : 250);
            string[] lines;
            string control;
            Func<int, int, RegionFrame>? layout;
            bool redraw, wipe, stop;
            ManualResetEventSlim[] waiting;
            lock (gate)
            {
                FlushPartial(force: false);
                lines = committed.ToArray();
                committed.Clear();
                layout = region;
                // One more frame after animation stops removes its last state.
                redraw = dirty || (animating || animated) && region is not null;
                animated = animating;
                control = controls.ToString();
                controls.Clear();
                wipe = clear;
                stop = stopping;
                dirty = clear = false;
                waiting = flushes.ToArray();
                flushes.Clear();
            }
            try
            {
                var (width, height) = device.Size;
                if ((width, height) != drawnSize && drawn is not null) redraw = true;
                if (lines.Length > 0 || redraw || wipe || stop)
                    device.Write(Render(lines, stop ? null : layout, width, height, wipe));
                if (control.Length > 0) device.Write(control);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
            {
                failure = error;
                foreach (var flushed in waiting) flushed.Set();
                return;
            }
            foreach (var flushed in waiting) flushed.Set();
            if (stop) return;
        }
    }

    private string Render(string[] lines, Func<int, int, RegionFrame>? layout, int width, int height, bool wipe)
    {
        var output = new StringBuilder(BeginFrame);
        output.Append(MoveToRegionStart(width));
        if (wipe) output.Append("\x1b[H\x1b[2J\x1b[3J");
        else output.Append("\x1b[J");
        foreach (var line in lines) output.Append(line).Append("\x1b[0m\r\n");
        drawn = null;
        if (layout is not null)
        {
            var frame = layout(width, height);
            for (var i = 0; i < frame.Rows.Count; i++)
            {
                if (i > 0) output.Append("\r\n");
                output.Append(frame.Rows[i]).Append("\x1b[0m\x1b[K");
            }
            output.Append("\x1b[J");
            var up = frame.Rows.Count - 1 - frame.CaretRow;
            if (up > 0) output.Append("\x1b[").Append(up).Append('A');
            output.Append('\r');
            if (frame.CaretColumn > 0) output.Append("\x1b[").Append(frame.CaretColumn).Append('C');
            output.Append("\x1b[?25h");
            drawn = frame;
            drawnSize = (width, height);
        }
        else output.Append("\x1b[?25h");
        return output.Append(EndFrame).ToString();
    }

    // The cursor rests on the caret. A narrower terminal may have rewrapped the rows above it.
    private string MoveToRegionStart(int width)
    {
        if (drawn is null) return "\r";
        var rows = 0;
        for (var i = 0; i < drawn.CaretRow; i++) rows += Math.Max(1, (CellWidth.Of(drawn.PlainRows[i]) + width - 1) / Math.Max(1, width));
        rows += drawn.CaretColumn / Math.Max(1, width);
        return rows > 0 ? $"\r\x1b[{rows}A" : "\r";
    }

    /// <summary>Removes cursor movement and other control sequences from foreign output, keeping colors.
    /// A carriage return within a line keeps only the text after it, as a terminal would show it.</summary>
    internal static string Clean(string line)
    {
        line = line.TrimEnd('\r');
        var carriage = line.LastIndexOf('\r');
        if (carriage >= 0) line = line[(carriage + 1)..];
        if (!line.Any(c => c < ' ' && c != '\t')) return line;
        line = ControlSequencePattern.Replace(line, match => match.Groups["sgr"].Value);
        return new string(line.Where(c => c >= ' ' || c is '\t' or '\x1b').ToArray());
    }

    // Only SGR sequences survive: every other escape, including a stray ESC, is dropped.
    private static readonly Regex ControlSequencePattern = new(@"(?<sgr>\x1b\[[0-9;:]*m)|\x1b\[[0-?]*[ -/]*[@-~]?|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)?|\x1b.?", RegexOptions.Singleline);

    /// <summary>Writes pending output, removes the region and leaves the cursor at the start of a free line.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (stopping) return;
            FlushPartial(force: true);
            stopping = true;
        }
        signal.Release();
        thread.Join();
        signal.Dispose();
    }
}
