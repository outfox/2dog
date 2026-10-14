using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using twodog.Repl;

namespace twodog.tests.ReplTests;

// These tests swap process-wide standard handles and Console writers; nothing else may run meanwhile.
[CollectionDefinition(nameof(ProcessConsoleCollection), DisableParallelization = true)]
public sealed class ProcessConsoleCollection;

[Collection(nameof(ProcessConsoleCollection))]
public class ConsoleTests
{
    [Fact]
    public void StandardOutputAndErrorIncludingNativeWritesReachTheSinkUntilDisposed()
    {
        var captured = new StringBuilder();
        var errors = new StringBuilder();
        var originalOut = Console.Out;
        using (var stdio = new ReplStdio((text, error) => { lock (captured) (error ? errors : captured).Append(text); }))
        {
            Console.Write('m');
            Console.Write("anaged ");
            Console.Out.Write("chars".ToCharArray(), 0, 5);
            Console.WriteLine();
            Console.Out.Write((string?)null);
            Console.Error.WriteLine("managed error");
            Assert.Equal(Encoding.UTF8, Console.Out.Encoding);
            Native.Write(error: false, "native out é\n");
            Native.Write(error: true, "ERROR: native error\n");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (captured)
                    if (captured.ToString().Contains("native out", StringComparison.Ordinal) && errors.ToString().Contains("native error", StringComparison.Ordinal)) break;
                Thread.Sleep(5);
            }
            stdio.Dispose();
        }
        Assert.Same(originalOut, Console.Out);
        Assert.Contains("managed chars" + Environment.NewLine, captured.ToString());
        Assert.Contains("native out é\n", captured.ToString());
        Assert.Contains("managed error", errors.ToString());
        Assert.Contains("ERROR: native error", errors.ToString());
    }

    [Fact]
    public void LogLinesFromGodotAreColoredByKind()
    {
        var styled = ReplOutput.StyleLog("ERROR: broken\n   at: Node.cs:1\nWARNING: careful\nUSER WARNING: hmm\nSCRIPT ERROR: x\nplain\n");
        Assert.Contains("\x1b[31mERROR: broken\x1b[0m", styled);
        Assert.Contains("\x1b[2m   at: Node.cs:1\x1b[0m", styled);
        Assert.Contains("\x1b[33mWARNING: careful\x1b[0m", styled);
        Assert.Contains("\x1b[33mUSER WARNING: hmm\x1b[0m", styled);
        Assert.Contains("\x1b[31mSCRIPT ERROR: x\x1b[0m", styled);
        Assert.Contains("\nplain\n", styled);
    }

    [Fact]
    public void TheConsoleDeviceDrawsOnTheTerminalItselfNotTheStandardHandles()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Unix terminals are exercised through a pseudo terminal, outside the test host.");
        // A test host may run without a console; give it one for the duration of the test.
        var handles = new[] { -10, -11, -12 }.Select(Native.GetStdHandle).ToArray();
        var allocated = !Native.HasConsole() && Native.AllocConsole();
        try
        {
            using var device = new ConsoleDevice();
            var (width, height) = device.Size;
            Assert.True(width > 0 && height > 0);
            device.Write("");
        }
        finally
        {
            if (allocated) Native.FreeConsole();
            for (var i = 0; i < handles.Length; i++) Native.SetStdHandle(-10 - i, handles[i]);
        }
    }

    [Fact]
    public async Task AnInteractiveConsoleCapturesProcessOutputIntoItsScreenAndRestoresIt()
    {
        var device = new VirtualTerminal(60, 10);
        var keys = new Queue<ConsoleKeyInfo>([Keys.Enter]);
        var originalOut = Console.Out;
        var console = new ReplConsole(interactive: true, () => device, new ReplKeyReader(() => keys.Count > 0, keys.Dequeue, windows: true), captureOutput: true);
        try
        {
            Assert.True(console.Interactive);
            Assert.Same(Console.In, console.Input);
            Assert.EndsWith("repl-history", console.HistoryFile, StringComparison.Ordinal);
            Assert.Equal(Keys.Enter, Assert.IsType<KeyInput>(await console.ReadAsync(TestContext.Current.CancellationToken)).Key);
            using var cancel = new CancellationTokenSource(50);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => console.ReadAsync(cancel.Token));
            console.Output.Message("from the repl");
            Native.Write(error: true, "ERROR: from godot\n");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!device.Text.Contains("ERROR: from godot", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            {
                console.Screen!.Flush();
                Thread.Sleep(5);
            }
        }
        finally { console.Restore(); }
        Assert.Same(originalOut, Console.Out);
        Assert.Contains("from the repl", device.Text);
        Assert.Contains("ERROR: from godot", device.Text);
        Assert.Contains("\x1b[31mERROR: from godot", device.Raw);
        var redirected = new ReplConsole(interactive: false, () => throw new InvalidOperationException(), ReplConsole.ConsoleKeys(), captureOutput: true);
        Assert.False(redirected.Interactive);
        Assert.Null(redirected.Screen);
        redirected.Restore();
    }

    [Fact]
    public async Task AClientConsoleReadsKeysAndReportsInterruptSignals()
    {
        var device = new VirtualTerminal(40, 8);
        var keys = new Queue<ConsoleKeyInfo>(Keys.Type("hi"));
        var client = new ConsoleClientTerminal(device, new ReplKeyReader(() => keys.Count > 0, keys.Dequeue, windows: true));
        try
        {
            Assert.Same(device, client.Device);
            Assert.Equal(ReplOutput.ColorEnabled, client.Color);
            Assert.Equal('h', Assert.IsType<KeyInput>(await client.ReadAsync(TestContext.Current.CancellationToken)).Key.KeyChar);
            var interrupted = 0;
            client.Interrupted += () => interrupted++;
            var args = (ConsoleCancelEventArgs)Activator.CreateInstance(typeof(ConsoleCancelEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: [ConsoleSpecialKey.ControlC], culture: null)!;
            client.OnCancel(null, args);
            Assert.True(args.Cancel);
            Assert.Equal(1, interrupted);
        }
        finally { client.Restore(); }
    }

    private static class Native
    {
        public static void Write(bool error, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (OperatingSystem.IsWindows()) WriteFile(GetStdHandle(error ? -12 : -11), bytes, bytes.Length, out _, IntPtr.Zero);
            else UnixWrite(error ? 2 : 1, bytes, bytes.Length);
        }

        public static bool HasConsole() => GetConsoleWindow() != IntPtr.Zero || GetConsoleMode(GetStdHandle(-11), out _);

        [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int id);
        [DllImport("kernel32.dll")] public static extern bool SetStdHandle(int id, IntPtr handle);
        [DllImport("kernel32.dll")] public static extern bool AllocConsole();
        [DllImport("kernel32.dll")] public static extern bool FreeConsole();
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        [DllImport("kernel32.dll")] private static extern bool WriteFile(IntPtr handle, byte[] bytes, int count, out int written, IntPtr overlapped);
        [DllImport("libc", EntryPoint = "write")] private static extern nint UnixWrite(int fd, byte[] bytes, nint count);
    }
}
