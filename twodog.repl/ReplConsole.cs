using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace twodog.Repl;

/// <summary>A REPL front end's terminal: line input for redirected streams, or keys and a screen when interactive.</summary>
internal interface IReplTerminal
{
    bool Interactive { get; }
    /// <summary>Redirected input, read a line at a time.</summary>
    TextReader Input { get; }
    ReplOutput Output { get; }
    /// <summary>Where an interactive terminal draws; output written through <see cref="Output"/> goes above its input region.</summary>
    ReplScreen? Screen => null;
    bool Color => ReplOutput.ColorEnabled;
    string HistoryFile { get; }
    /// <summary>The next key or paste of an interactive terminal.</summary>
    Task<TerminalInput> ReadAsync(CancellationToken token) => throw new NotSupportedException("This terminal reads lines.");
    /// <summary>Cancellation requested outside the editor, such as a remote client's interrupt signal.</summary>
    void SetCancellationHandler(Action? cancel) { }
    void Restore();
}

/// <summary>The process console. Interactive use owns the terminal: every write, including native output from Godot, is
/// redirected into a <see cref="ReplScreen"/> so it appears above the input region instead of inside the draft.</summary>
internal sealed class ReplConsole : IReplTerminal
{
    public bool Interactive { get; }
    public TextReader Input => Console.In;
    public ReplOutput Output { get; }
    public ReplScreen? Screen { get; }
    public string HistoryFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2dog", "repl-history");
    private readonly ITerminalDevice? device;
    private readonly ReplStdio? stdio;
    private readonly ReplKeyReader keys;
    private readonly bool? originalControlC;

    public ReplConsole(CancellationToken lifetime)
        : this(!Console.IsInputRedirected && !Console.IsOutputRedirected, () => new ConsoleDevice(), ConsoleKeys(), captureOutput: true) { }

    internal ReplConsole(bool interactive, Func<ITerminalDevice> openDevice, ReplKeyReader keys, bool captureOutput)
    {
        Interactive = interactive;
        this.keys = keys;
        if (!interactive)
        {
            Output = ReplOutput.ForConsole();
            return;
        }
        device = openDevice();
        var screen = Screen = new ReplScreen(device, ReplOutput.ColorEnabled);
        if (captureOutput) stdio = new ReplStdio((text, error) => screen.Write(error ? ReplOutput.StyleLog(text) : text, error));
        Output = new ReplOutput(new ScreenWriter(screen, false), new ScreenWriter(screen, true), screen.Color, screen.Color);
        // Ctrl+C reaches the editor as a key, which cancels the session's work.
        originalControlC = ControlCAsInput(true);
    }

    internal static ReplKeyReader ConsoleKeys()
        => new(() => Console.KeyAvailable, () => Console.ReadKey(intercept: true), OperatingSystem.IsWindows());

    /// <summary>Sets Console.TreatControlCAsInput; returns its previous value, or null without console input.</summary>
    internal static bool? ControlCAsInput(bool enabled)
    {
        if (Console.IsInputRedirected) return null;
        var original = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = enabled;
        return original;
    }

    public Task<TerminalInput> ReadAsync(CancellationToken token) => ReadKeysAsync(keys, token);

    /// <summary>Polls, so that cancellation never leaves a thread blocked in Console.ReadKey.</summary>
    internal static async Task<TerminalInput> ReadKeysAsync(ReplKeyReader keys, CancellationToken token)
    {
        var idle = false;
        while (!keys.Available)
        {
            idle = true;
            await Task.Delay(10, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return keys.Read(idle);
    }

    public void Restore()
    {
        if (!Interactive) return;
        Screen!.Dispose();
        stdio?.Dispose();
        (device as IDisposable)?.Dispose();
        if (originalControlC is { } controlC) ControlCAsInput(controlC);
    }
}

/// <summary>Writes into a screen's output area.</summary>
internal sealed class ScreenWriter(ReplScreen screen, bool error) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char value) => screen.Write(value.ToString(), error);
    public override void Write(string? value) => screen.Write(value, error);
    public override void Write(char[] buffer, int index, int count) => screen.Write(new string(buffer, index, count), error);
    public override void WriteLine(string? value) => screen.Write(value + NewLine, error);
}

/// <summary>The real terminal, independent of the standard handles: CONOUT$ on Windows, a duplicate of stdout elsewhere.
/// Enables the terminal modes the editor relies on and restores them on disposal.</summary>
internal sealed class ConsoleDevice : ITerminalDevice, IDisposable
{
    private const string UnixModes = "\x1b[?2004h\x1b[>4;1m\x1b[>1u", UnixRestore = "\x1b[<u\x1b[>4;0m\x1b[?2004l", CursorRestore = "\x1b[0 q";
    private readonly SafeFileHandle handle;
    private readonly FileStream? stream;
    private readonly uint originalMode;
    private (int Width, int Height) lastSize = (80, 24);

    public ConsoleDevice()
    {
        if (OperatingSystem.IsWindows())
        {
            handle = CreateFileW("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("Could not open the console output.", Marshal.GetLastPInvokeError());
            GetConsoleMode(handle, out originalMode);
            // Virtual terminal processing; the editor writes explicit carriage returns.
            SetConsoleMode(handle, originalMode | 0x1 | 0x4 | 0x8);
        }
        else
        {
            handle = new SafeFileHandle(Dup(1), ownsHandle: true);
            stream = new FileStream(handle, FileAccess.Write, 1);
            Write(UnixModes);
        }
    }

    public (int Width, int Height) Size
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (GetConsoleScreenBufferInfo(handle, out var info))
                        lastSize = (info.Right - info.Left + 1, info.Bottom - info.Top + 1);
                }
                // The runtime's own TIOCGWINSZ shim; ioctl itself is variadic and unsafe to call directly on arm64.
                else if (GetWindowSize(handle.DangerousGetHandle(), out var size) == 0 && size.Columns > 0)
                    lastSize = (size.Columns, size.Rows);
            }
            catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException) { }
            return lastSize;
        }
    }

    public void Write(string ansi)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!WriteConsoleW(handle, ansi, ansi.Length, out _, IntPtr.Zero)) throw new IOException("Could not write to the console.", Marshal.GetLastPInvokeError());
            return;
        }
        stream!.Write(Encoding.UTF8.GetBytes(ansi));
        stream.Flush();
    }

    public void Dispose()
    {
        try
        {
            // A host that went away may have left an overwrite-mode block cursor behind.
            Write(CursorRestore);
            if (OperatingSystem.IsWindows()) SetConsoleMode(handle, originalMode);
            else Write(UnixRestore);
        }
        finally
        {
            stream?.Dispose();
            handle.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferInfo
    {
        public short SizeX, SizeY, CursorX, CursorY;
        public ushort Attributes;
        public short Left, Top, Right, Bottom;
        public short MaxX, MaxY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSize
    {
        public ushort Rows, Columns, Width, Height;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(SafeFileHandle handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(SafeFileHandle handle, uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleScreenBufferInfo(SafeFileHandle handle, out BufferInfo info);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteConsoleW(SafeFileHandle handle, string text, int length, out int written, IntPtr reserved);

    [DllImport("libc", EntryPoint = "dup")]
    private static extern int Dup(int fd);

    [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetWindowSize")]
    private static extern int GetWindowSize(IntPtr fd, out WindowSize size);
}
