using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace twodog;

/// <summary>
/// Keeps the host's NSApplication intact across engine startup on macOS.
/// </summary>
/// <remarks>
/// libgodot's macOS entry constructs <c>OS_MacOS_NSApp</c>, whose constructor is written for
/// a process Godot owns: it replaces the main menu and installs <c>GodotApplicationDelegate</c>
/// as the NSApp delegate, evicting Avalonia's. That delegate treats
/// <c>applicationDidFinishLaunching</c> as "boot the engine" (a second <c>Main::setup</c> -
/// "singleton already exists" errors, then a CFRunLoop observer iterating the engine behind
/// the session's pump) and <c>application:openFiles:</c> as "spawn a new instance" - and
/// Cocoa hands every bare command-line argument to that handler as a document, so a host
/// started as <c>app --quit-after 200</c> relaunches itself with <c>200</c>, forever.
/// Snapshotting the delegate and menu before <c>Engine.Start()</c> and restoring them after
/// leaves Godot's delegate installed nowhere: Avalonia keeps owning the application, and the
/// engine's hidden window keeps working through the display server alone.
/// </remarks>
[SupportedOSPlatform("macos")]
internal readonly struct MacOSApplicationGuard
{
    private readonly nint _app;
    private readonly nint _delegate;
    private readonly nint _mainMenu;

    private MacOSApplicationGuard(nint app, nint del, nint mainMenu)
    {
        _app = app;
        _delegate = del;
        _mainMenu = mainMenu;
    }

    /// <summary>Snapshots the current NSApp delegate and main menu; null when there is no
    /// NSApplication yet (a host that never touched AppKit has nothing to protect).</summary>
    public static MacOSApplicationGuard? Capture()
    {
        var app = objc_msgSend(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        if (app == 0) return null;
        var del = objc_msgSend(app, sel_registerName("delegate"));
        var menu = objc_msgSend(app, sel_registerName("mainMenu"));
        // Retain what we hand back later: the engine's takeover may be the last strong reference
        // AppKit itself holds on the previous main menu.
        if (del != 0) objc_msgSend(del, sel_registerName("retain"));
        if (menu != 0) objc_msgSend(menu, sel_registerName("retain"));
        return new MacOSApplicationGuard(app, del, menu);
    }

    /// <summary>Puts the snapshotted delegate and main menu back and drops the retains.</summary>
    public void Restore()
    {
        objc_msgSend(_app, sel_registerName("setDelegate:"), _delegate);
        objc_msgSend(_app, sel_registerName("setMainMenu:"), _mainMenu);
        if (_delegate != 0) objc_msgSend(_delegate, sel_registerName("release"));
        if (_mainMenu != 0) objc_msgSend(_mainMenu, sel_registerName("release"));
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjC)]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport(ObjC)]
    private static extern nint objc_msgSend(nint receiver, nint selector, nint arg);
}
