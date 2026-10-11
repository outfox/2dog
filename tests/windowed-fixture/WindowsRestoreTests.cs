using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.Windowed;

[Collection<RenderingCollection>]
public class WindowsRestoreTests(Fixture godot, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void QueuedRestoreReturnsMinimizedWindowOnEveryMonitor(bool maximized, bool minimizeThroughGodot)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = godot.Tree.Root;
        var originalMode = root.Mode;
        var originalPosition = root.Position;
        var originalSize = root.Size;
        var originalScreen = root.CurrentScreen;
        var hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        try
        {
            for (var screen = 0; screen < DisplayServer.GetScreenCount(); screen++)
            {
                root.Mode = Window.ModeEnum.Windowed;
                root.CurrentScreen = screen;
                root.Mode = maximized ? Window.ModeEnum.Maximized : Window.ModeEnum.Windowed;
                PumpUntil(() => root.CurrentScreen == screen && (maximized ? IsZoomed(hwnd) : !IsIconic(hwnd)));
                output.WriteLine($"screen={screen}; dpi={DisplayServer.ScreenGetDpi(screen)}; maximized={maximized}; minimizeThroughGodot={minimizeThroughGodot}");
                // Use Windows' system command path when restoring, rather than Godot's
                // mode setter (which could conceal stale native state).
                if (minimizeThroughGodot) root.Mode = Window.ModeEnum.Minimized;
                else Assert.True(PostMessage(hwnd, 0x0112, 0xF020, 0)); // WM_SYSCOMMAND / SC_MINIMIZE
                PumpUntil(() => IsIconic(hwnd));
                Assert.Equal(Window.ModeEnum.Minimized, root.Mode);
                var before = Godot.Engine.GetFramesDrawn();
                Assert.True(PostMessage(hwnd, 0x0112, 0xF120, 0)); // WM_SYSCOMMAND / SC_RESTORE
                PumpUntil(() => !IsIconic(hwnd) && Godot.Engine.GetFramesDrawn() > before);
                Assert.True(IsWindowVisible(hwnd));
                Assert.Equal(maximized, IsZoomed(hwnd));
                Assert.Equal(maximized ? Window.ModeEnum.Maximized : Window.ModeEnum.Windowed, root.Mode);
                Assert.Equal(screen, root.CurrentScreen);
                Assert.True(GetWindowRect(hwnd, out var rect));
                Assert.True(MonitorFromRect(ref rect, 0) != 0, "Restored window does not intersect a monitor.");
            }
        }
        finally
        {
            root.Mode = Window.ModeEnum.Windowed;
            root.CurrentScreen = originalScreen;
            root.Position = originalPosition;
            root.Size = originalSize;
            root.Mode = originalMode;
        }
    }

    [Fact]
    public async Task AsynchronousRestoreFromAnotherThreadReachesWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = godot.Tree.Root;
        var originalMode = root.Mode;
        var hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        try
        {
            root.Mode = Window.ModeEnum.Windowed;
            root.Mode = Window.ModeEnum.Minimized;
            PumpUntil(() => IsIconic(hwnd));
            var before = Godot.Engine.GetFramesDrawn();
            // Unlike a posted SC_RESTORE, this uses Windows' asynchronous window
            // management path to the owner thread's input queue.
            Assert.True(await Task.Run(() => ShowWindowAsync(hwnd, 9))); // SW_RESTORE
            PumpUntil(() => !IsIconic(hwnd) && Godot.Engine.GetFramesDrawn() > before);
        }
        finally { root.Mode = originalMode; }
    }

    [Fact]
    public void QueuedSystemCommandsRestoreSameWindowAfterRepeatedMinimizations()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = godot.Tree.Root;
        var originalMode = root.Mode;
        var hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        try
        {
            root.Mode = Window.ModeEnum.Windowed;
            // Repeat on one window/monitor, including beyond the third restore.
            // This isolates cycle count; it does not emulate shell taskbar activation.
            for (var cycle = 0; cycle < 10; cycle++)
            {
                Assert.True(PostMessage(hwnd, 0x0112, 0xF020, 0));
                PumpUntil(() => IsIconic(hwnd));
                var before = Godot.Engine.GetFramesDrawn();
                Assert.True(PostMessage(hwnd, 0x0112, 0xF120, 0));
                PumpUntil(() => !IsIconic(hwnd) && Godot.Engine.GetFramesDrawn() > before);
                Assert.Equal(Window.ModeEnum.Windowed, root.Mode);
            }
        }
        finally { root.Mode = originalMode; }
    }

    [Fact]
    public void MinimizedWindowKeepsProcessingMessagesPastWindowsHungTimeout()
    {
        if (!OperatingSystem.IsWindows()) return;
        var hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        var root = godot.Tree.Root;
        var originalMode = root.Mode;
        try
        {
            root.Mode = Window.ModeEnum.Minimized;
            PumpUntil(() => IsIconic(hwnd));
            var elapsed = Stopwatch.StartNew();
            // Windows considers an unpumped window hung after roughly five seconds.
            // Rendering is suspended here, but lifecycle messages must keep flowing.
            while (elapsed.Elapsed < TimeSpan.FromSeconds(7))
            {
                Assert.False(godot.Engine.Iteration());
                Thread.Sleep(10);
            }
            Assert.False(IsHungAppWindow(hwnd));
            var before = Godot.Engine.GetFramesDrawn();
            Assert.True(PostMessage(hwnd, 0x0112, 0xF120, 0));
            PumpUntil(() => !IsIconic(hwnd) && Godot.Engine.GetFramesDrawn() > before);
        }
        finally { root.Mode = originalMode; }
    }

    private void PumpUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(5), "Windows did not complete the window transition.");
            Assert.False(godot.Engine.Iteration());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsHungAppWindow(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hwnd, int command);
}
