using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Godot;
using FileAccess = System.IO.FileAccess;
using Timer = System.Threading.Timer;

namespace twodog;

// Diagnostic only: no file IO or synchronous console writes in the window procedure.
// The observer never calls Godot or sends window messages, so a stalled owner thread
// cannot prevent the heartbeat or be woken up by it.
internal sealed unsafe class WindowsLifecycleTrace : IDisposable
{
    private readonly Window _root;
    private readonly nint _hwnd;
    private readonly StreamWriter _writer;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly object _writeSync = new();
    private readonly Timer _timer;
    private readonly long _origin = Stopwatch.GetTimestamp();
    private GCHandle _self;
    private int _queued;
    private int _dropped;
    private long _iterations;
    private long _phaseSince = Stopwatch.GetTimestamp();
    private string _phase = "host";
    private bool _subclassInstalled;
    private bool _disposed;

    private static nint Callback => (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint>)&WindowProc;

    internal static WindowsLifecycleTrace? TryStart()
    {
        var path = System.Environment.GetEnvironmentVariable("TWODOG_LIFECYCLE_LOG");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || DisplayServer.GetName() != "Windows")
            return null;
        try
        {
            if (Godot.Engine.Singleton.GetMainLoop() is not SceneTree tree) return null;
            var fullPath = Path.GetFullPath(path);
            var trace = new WindowsLifecycleTrace(tree.Root, fullPath);
            Console.WriteLine($"Engine: Window lifecycle trace: {fullPath}");
            return trace;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Engine: Could not open lifecycle trace: {error.Message}");
            return null;
        }
    }

    private WindowsLifecycleTrace(Window root, string path)
    {
        _root = root;
        _hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, root.GetWindowId());
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        _timer = new Timer(Observe, null, Timeout.Infinite, Timeout.Infinite);
        _self = GCHandle.Alloc(this);
        _subclassInstalled = SetWindowSubclass(_hwnd, Callback, 1, (nuint)GCHandle.ToIntPtr(_self));
        if (!_subclassInstalled) _self.Free();
        _root.FocusEntered += FocusIn;
        _root.FocusExited += FocusOut;
        _root.SizeChanged += SizeChanged;
        _root.VisibilityChanged += VisibilityChanged;
        Log($"started engine={Engine.Version} ownerThread={System.Environment.CurrentManagedThreadId} hwnd=0x{_hwnd:X} subclass={_subclassInstalled}");
        GodotState("initial");
        _timer.Change(0, 1000);
    }

    internal void IterationStarted() => Phase("Godot iteration");

    internal void IterationEnded()
    {
        var duration = Stopwatch.GetElapsedTime(Volatile.Read(ref _phaseSince));
        Interlocked.Increment(ref _iterations);
        Phase("host");
        if (duration >= TimeSpan.FromSeconds(1)) Log($"slow-iteration elapsedMs={duration.TotalMilliseconds:F0}");
    }

    internal void Phase(string phase)
    {
        Volatile.Write(ref _phaseSince, Stopwatch.GetTimestamp());
        Volatile.Write(ref _phase, phase);
    }

    internal void Log(string message)
    {
        if (Interlocked.Increment(ref _queued) > 2048)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _dropped);
            return;
        }
        _pending.Enqueue($"{DateTimeOffset.UtcNow:O} pid={System.Environment.ProcessId} ms={Stopwatch.GetElapsedTime(_origin).TotalMilliseconds:F1} {message}");
    }

    private void FocusIn() => GodotState("focus-in");
    private void FocusOut() => GodotState("focus-out");
    private void SizeChanged() => GodotState("size-changed");
    private void VisibilityChanged() => GodotState("visibility-changed");
    private void GodotState(string reason) => Log($"godot {reason} mode={_root.Mode} visible={_root.Visible} focus={_root.HasFocus()} position={_root.Position} size={_root.Size} screen={_root.CurrentScreen}");

    private void Observe(object? state)
    {
        lock (_writeSync)
        {
            if (_disposed) return;
            try
            {
                var foreground = GetForegroundWindow();
                var placement = new Placement { Length = (uint)sizeof(Placement) };
                var placed = GetWindowPlacement(_hwnd, ref placement);
                Log($"heartbeat phase=\"{Volatile.Read(ref _phase)}\" phaseAgeMs={Stopwatch.GetElapsedTime(Volatile.Read(ref _phaseSince)).TotalMilliseconds:F0} iterations={Interlocked.Read(ref _iterations)} visible={IsWindowVisible(_hwnd)} minimized={IsIconic(_hwnd)} maximized={IsZoomed(_hwnd)} hung={IsHungAppWindow(_hwnd)} foreground=0x{foreground:X} showCommand={(placed ? placement.ShowCommand : 0)} restore=({placement.Normal.Left},{placement.Normal.Top},{placement.Normal.Right},{placement.Normal.Bottom})");
                Drain();
            }
            catch (Exception)
            {
                // A diagnostic IO failure must not terminate the host on a timer thread.
                try { _timer.Change(Timeout.Infinite, Timeout.Infinite); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private void Drain()
    {
        while (_pending.TryDequeue(out var line))
        {
            Interlocked.Decrement(ref _queued);
            _writer.WriteLine(line);
        }
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0) _writer.WriteLine($"trace dropped={dropped}");
        _writer.Flush();
    }

    public void Dispose()
    {
        _root.FocusEntered -= FocusIn;
        _root.FocusExited -= FocusOut;
        _root.SizeChanged -= SizeChanged;
        _root.VisibilityChanged -= VisibilityChanged;
        if (_subclassInstalled && RemoveWindowSubclass(_hwnd, Callback, 1))
        {
            _subclassInstalled = false;
            _self.Free();
        }
        _timer.Dispose();
        lock (_writeSync)
        {
            _disposed = true;
            try { Log("trace-detached before native destruction"); Drain(); }
            catch (IOException) { }
            try { _writer.Dispose(); }
            catch (IOException) { }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        // Forward every message, including lifecycle commands, without changing its result.
        WindowsLifecycleTrace? trace = null;
        string? name = null;
        try
        {
            trace = (WindowsLifecycleTrace)GCHandle.FromIntPtr((nint)data).Target!;
            name = message switch
            {
                0x0005 => "WM_SIZE", 0x0006 => "WM_ACTIVATE", 0x0007 => "WM_SETFOCUS",
                0x0008 => "WM_KILLFOCUS", 0x0010 => "WM_CLOSE", 0x0018 => "WM_SHOWWINDOW",
                0x001C => "WM_ACTIVATEAPP", 0x0046 => "WM_WINDOWPOSCHANGING",
                0x0047 => "WM_WINDOWPOSCHANGED", 0x0082 => "WM_NCDESTROY",
                0x0112 => "WM_SYSCOMMAND", 0x02E0 => "WM_DPICHANGED", _ => null
            };
            if (name is not null)
                trace.Log($"win32 {name} wParam=0x{wParam:X} lParam=0x{lParam:X} minimized={IsIconic(hwnd)}");
            if (message == 0x0082 && trace._subclassInstalled)
            {
                RemoveWindowSubclass(hwnd, Callback, id);
                trace._subclassInstalled = false;
                trace._self.Free();
            }
        }
        catch (Exception) { /* Never unwind a managed exception through a window procedure. */ }
        var result = DefSubclassProc(hwnd, message, wParam, lParam);
        try
        {
            if (name is not null) trace?.Log($"win32-return {name} result=0x{result:X} minimized={IsIconic(hwnd)}");
        }
        catch (Exception) { }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public uint Length, Flags, ShowCommand;
        public Point Minimum, Maximum;
        public Rect Normal;
    }

    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, nint callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, nint callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
}
