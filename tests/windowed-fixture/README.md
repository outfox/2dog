Starts the stock windowed `Fixture` through `RenderingCollection`, as consumers' rendering suites do, and renders a
few frames. It guards the macOS case where AppKit aborts the test process (exit code 134) unless the engine and its
window start on the process main thread, which 2dog.xunit's entry point gives to the 2dog test thread.

It needs a display, so it is not part of `2dog.tests.slnf`. CI runs it on macOS against the packed packages:

```bash
dotnet test tests/windowed-fixture -c Release
```

On macOS the fixture uses the Mobile renderer with Metal. Native OpenGL is unavailable on the CI virtual machine;
the test still creates a real window and renders frames through the stock fixture.

The project selects the Dummy audio driver because this rendering-only test does not need an audio device.

On Windows, the suite also checks minimize/restore from normal and maximized
windows, through both Godot and native system commands, on every attached
monitor. It verifies that the window returns on the same monitor and resumes
rendering. A longer minimized interval checks that message processing continues
past Windows' hung-window timeout. Another test restores the same window ten
times through queued system commands; a separate case restores asynchronously
from another thread with `ShowWindowAsync`. These tests need an interactive desktop.

For an intermittent window that refuses to return from the taskbar, leave the
process running and capture its native state from another PowerShell terminal:

```powershell
Get-Process showcase.2dog | Select-Object Id, ProcessName
./scripts/diagnose_windows_window.ps1 -TargetProcessId 1234 -DurationSeconds 30 |
    Tee-Object -FilePath window-state.jsonl
```

Run the script from the repository root and substitute the game's process ID.
It does not activate, restore, or move the window. It records native visibility,
minimized/maximized state, responsiveness, ghost-window handle, DPI awareness,
window placement, monitor intersection, and CPU time as JSON lines. Window and
monitor bounds use physical pixels; restore bounds are Windows' native placement
coordinates. A minimized window's current bounds can legitimately be off-screen.

Capture while clicking the taskbar entry. `Minimized: true` after the click means
the restore did not complete; a restored window with `IntersectsMonitor: false`
points to placement; persistent `RespondsWithin200ms: false` and `Hung: true`
point to a stalled window thread. One timeout during startup is not sufficient
evidence of a hang. `Cloaked` records DWM/virtual-desktop hiding independently of
ordinary window visibility. This diagnostic does not measure rendered frames.

To trace the actual taskbar activation path inside 2dog, set this before starting
the plain showcase (from `demos/showcase`):

```powershell
$env:TWODOG_LIFECYCLE_LOG = 'P:\2dog\window-lifecycle.log'
dotnet run --project showcase.2dog
```

In Nushell, use `$env.TWODOG_LIFECYCLE_LOG = 'P:\2dog\window-lifecycle.log'`
for the assignment instead.

Repeat taskbar minimize/restore until it sticks, then leave it running. Use a
separate log path for each simultaneously running process. The log appends
Windows messages (including `WM_SYSCOMMAND`, `WM_ACTIVATEAPP`, focus,
visibility and position changes), Godot window signals, and a heartbeat every
second. `WM_SYSCOMMAND` values `0xF020` and `0xF120` mean minimize and restore.
`win32-return` confirms Godot's window procedure returned from that message.
The heartbeat shows completed engine iterations, whether execution is inside
Godot or back in the host, native minimized state and Windows' hung detection.
It runs on a separate thread without calling Godot or sending window messages.
File writes happen on that thread too, outside the window procedure. No native
activation or restore behavior is changed. Remove the variable afterwards:

```powershell
Remove-Item Env:TWODOG_LIFECYCLE_LOG
```

Tracing is opt-in and currently supports the Windows display driver. The
automated system-command tests above do not simulate clicking the taskbar;
capture that sequence manually to distinguish its activation path.

For the taskbar restore regression, start a fresh plain showcase, then use its
taskbar button for both minimize and restore. Repeat at least ten cycles, also
while moving the mouse, and check normal and maximized windows on the attached
monitors. Repeat with `-- --verbose`; startup logging previously changed how
soon the failure appeared. The trace should show activation followed by
`SC_RESTORE` and native minimized state clearing. Check the animation visually
to confirm rendering resumed.
When comparing native message pumps, restart between implementations: replacing
the pump after a window is already stuck may not recover its existing state.
