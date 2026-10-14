using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace twodog.Repl;

/// <summary>Redirects the process's standard output and error, including native writes such as Godot's loggers and
/// other libraries, into <paramref name="sink"/> until disposed. Console.Out and Console.Error follow too.</summary>
internal sealed class ReplStdio : IDisposable
{
    private readonly Action<string, bool> sink;
    private readonly Stream[] streams = new Stream[2];
    private readonly Thread[] pumps = new Thread[2];
    private readonly Restore[] restore = new Restore[2];
    private readonly TextWriter originalOut = Console.Out, originalError = Console.Error;
    private int disposed;

    private delegate void Restore();

    public ReplStdio(Action<string, bool> sink)
    {
        this.sink = sink;
        for (var i = 0; i < 2; i++)
        {
            var error = i == 1;
            streams[i] = OperatingSystem.IsWindows() ? RedirectWindows(error, out restore[i]) : RedirectUnix(error, out restore[i]);
            var stream = streams[i];
            pumps[i] = new Thread(() => Pump(stream, error)) { IsBackground = true, Name = "2dog REPL " + (error ? "stderr" : "stdout") };
            pumps[i].Start();
        }
        Console.SetOut(TextWriter.Synchronized(new SinkWriter(sink, false)));
        Console.SetError(TextWriter.Synchronized(new SinkWriter(sink, true)));
    }

    private void Pump(Stream stream, bool error)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        try
        {
            int read;
            while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0);
                if (count > 0) sink(new string(chars, 0, count), error);
            }
        }
        catch (IOException) { /* The write end closed. */ }
        catch (ObjectDisposedException) { }
    }

    private static Stream RedirectWindows(bool error, out Restore restore)
    {
        var id = error ? StdErrorHandle : StdOutputHandle;
        var original = GetStdHandle(id);
        if (!CreatePipe(out var read, out var write, IntPtr.Zero, 0)) throw new IOException("Could not create an output pipe.", Marshal.GetLastPInvokeError());
        SetStdHandle(id, write.DangerousGetHandle());
        restore = () =>
        {
            SetStdHandle(id, original);
            write.Dispose();
        };
        return new FileStream(read, FileAccess.Read, 1);
    }

    private static Stream RedirectUnix(bool error, out Restore restore)
    {
        var fd = error ? 2 : 1;
        var original = Dup(fd);
        var pipe = new int[2];
        if (original < 0 || Pipe(pipe) != 0) throw new IOException("Could not create an output pipe.");
        // glibc picks stdio buffering on first use; keep native line output prompt once it is a pipe.
        FlushStdio();
        Dup2(pipe[1], fd);
        Close(pipe[1]);
        if (!error) LineBuffer();
        restore = () =>
        {
            FlushStdio();
            Dup2(original, fd);
            Close(original);
        };
        return new FileStream(new SafeFileHandle(pipe[0], ownsHandle: true), FileAccess.Read, 1);
    }

    // Writes from code that already holds Console.Out (e.g. a router installed earlier) still reach the sink.
    private sealed class SinkWriter(Action<string, bool> sink, bool error) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => sink(value.ToString(), error);
        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value)) sink(value, error);
        }
        public override void Write(char[] buffer, int index, int count) => sink(new string(buffer, index, count), error);
        public override void WriteLine(string? value) => sink(value + NewLine, error);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        foreach (var undo in restore) undo();
        foreach (var pump in pumps) pump.Join();
        foreach (var stream in streams) stream.Dispose();
    }

    private const int StdOutputHandle = -11, StdErrorHandle = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int id);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int id, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);

    [DllImport("libc", EntryPoint = "dup")]
    private static extern int Dup(int fd);

    [DllImport("libc", EntryPoint = "dup2")]
    private static extern int Dup2(int fd, int target);

    [DllImport("libc", EntryPoint = "pipe")]
    private static extern int Pipe([Out] int[] fds);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "fflush")]
    private static extern int Fflush(IntPtr stream);

    [DllImport("libc", EntryPoint = "setvbuf")]
    private static extern int Setvbuf(IntPtr stream, IntPtr buffer, int mode, nuint size);

    private static void FlushStdio() => Fflush(IntPtr.Zero);

    private static void LineBuffer()
    {
        var libc = NativeLibrary.Load(OperatingSystem.IsMacOS() ? "libc.dylib" : "libc.so.6");
        var symbol = NativeLibrary.GetExport(libc, OperatingSystem.IsMacOS() ? "__stdoutp" : "stdout");
        // _IOLBF is 1 on Linux and macOS.
        Setvbuf(Marshal.ReadIntPtr(symbol), IntPtr.Zero, 1, 0);
    }
}
