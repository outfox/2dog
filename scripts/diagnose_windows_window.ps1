<#
.SYNOPSIS
Capture a process's native Windows window state without activating or moving it.
.EXAMPLE
./scripts/diagnose_windows_window.ps1 -TargetProcessId 1234 -DurationSeconds 30 |
    Tee-Object -FilePath window-state.jsonl
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$TargetProcessId,
    [ValidateRange(0, 3600)]
    [int]$DurationSeconds = 0,
    [ValidateRange(100, 60000)]
    [int]$IntervalMilliseconds = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'This diagnostic requires Windows.'
}

if (-not ('TwoDog.Diagnostics.WindowSnapshot' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TwoDog.Diagnostics
{
    public static class WindowSnapshot
    {
        private delegate bool EnumWindow(nint hwnd, nint parameter);
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Placement
        {
            public uint Length, Flags, ShowCommand;
            public Point Minimum, Maximum;
            public Rect Normal;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public uint Size;
            public Rect Monitor, WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")] private static extern bool IsHungAppWindow(nint hwnd);
        [DllImport("user32.dll")] private static extern nint GhostWindowFromHungWindow(nint hwnd);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
        [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
        [DllImport("user32.dll")] private static extern nint GetWindowDpiAwarenessContext(nint hwnd);
        [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(nint context);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);
        [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Rect rect, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
        private static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wParam, nint lParam,
            uint flags, uint timeout, out nuint result);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out uint value, uint size);

        public static object[] Capture(uint targetProcessId)
        {
            // GetWindowRect virtualizes coordinates for the observing thread's DPI.
            // Use physical pixels consistently without changing the target process.
            nint previous = SetThreadDpiAwarenessContext(new nint(-4)); // PER_MONITOR_AWARE_V2
            try { return CaptureWindows(targetProcessId); }
            finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
        }

        private static object[] CaptureWindows(uint targetProcessId)
        {
            var windows = new List<object>();
            EnumWindows((hwnd, parameter) =>
            {
                uint thread = GetWindowThreadProcessId(hwnd, out uint owner);
                if (owner != targetProcessId) return true;
                var className = new StringBuilder(256);
                var title = new StringBuilder(1024);
                GetClassName(hwnd, className, className.Capacity);
                GetWindowText(hwnd, title, title.Capacity);
                bool visible = IsWindowVisible(hwnd);
                if (!visible && className.ToString() != "Engine") return true;
                bool rectAvailable = GetWindowRect(hwnd, out Rect rect);
                var placement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
                bool placementAvailable = GetWindowPlacement(hwnd, ref placement);
                nint monitor = rectAvailable ? MonitorFromRect(ref rect, 0) : 0;
                var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                bool monitorAvailable = monitor != 0 && GetMonitorInfo(monitor, ref monitorInfo);
                bool responsive = SendMessageTimeout(hwnd, 0, 0, 0, 0x0003, 200, out _) != 0;
                int cloakedStatus = DwmGetWindowAttribute(hwnd, 14, out uint cloaked, sizeof(uint));
                nint ghost = 0;
                try { ghost = GhostWindowFromHungWindow(hwnd); }
                catch (EntryPointNotFoundException) { }
                windows.Add(new
                {
                    Handle = $"0x{hwnd:X}", ThreadId = thread, Title = title.ToString(), Class = className.ToString(),
                    Visible = visible, Minimized = IsIconic(hwnd), Maximized = IsZoomed(hwnd),
                    RespondsWithin200ms = responsive, Hung = IsHungAppWindow(hwnd), Ghost = $"0x{ghost:X}",
                    Dpi = GetDpiForWindow(hwnd),
                    DpiAwareness = GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd)),
                    Cloaked = cloakedStatus == 0 ? (uint?)cloaked : null,
                    Bounds = rectAvailable ? (Rect?)rect : null,
                    RestoreBounds = placementAvailable ? (Rect?)placement.Normal : null,
                    ShowCommand = placementAvailable ? (uint?)placement.ShowCommand : null,
                    IntersectsMonitor = monitor != 0,
                    Monitor = monitorAvailable ? monitorInfo.Device : null,
                    MonitorBounds = monitorAvailable ? (Rect?)monitorInfo.Monitor : null,
                    WorkArea = monitorAvailable ? (Rect?)monitorInfo.WorkArea : null
                });
                return true;
            }, 0);
            return windows.ToArray();
        }
    }
}
'@
}

$elapsed = [System.Diagnostics.Stopwatch]::StartNew()
do {
    $targetProcess = Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue
    if ($null -eq $targetProcess) {
        [ordered]@{ Time = [DateTimeOffset]::Now.ToString('o'); ProcessId = $TargetProcessId; Exited = $true } |
            ConvertTo-Json -Compress
        break
    }
    [ordered]@{
        Time = [DateTimeOffset]::Now.ToString('o')
        ProcessId = $TargetProcessId
        ProcessName = $targetProcess.ProcessName
        CpuSeconds = $targetProcess.TotalProcessorTime.TotalSeconds
        Windows = @([TwoDog.Diagnostics.WindowSnapshot]::Capture([uint32]$TargetProcessId))
    } | ConvertTo-Json -Depth 6 -Compress
    if ($elapsed.Elapsed.TotalSeconds -ge $DurationSeconds) { break }
    Start-Sleep -Milliseconds $IntervalMilliseconds
} while ($true)
