using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;

namespace twodog;

internal static unsafe class X11EventQueue
{
    internal static void Initialize()
    {
        if (!OperatingSystem.IsLinux() || DisplayServer.GetName() != "X11") return;
        var display = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.DisplayHandle);
        // libX11 leaves ignored GenericEvent cookies uninitialized and can free garbage during XCloseDisplay.
        // Registered extensions use their own cookie converters; this only replaces the ignored-event fallback.
        XESetWireToEvent(display, 35 /* GenericEvent */, &IgnoreUnknownEvent);
    }

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint XESetWireToEvent(nint display, int eventNumber,
        delegate* unmanaged[Cdecl]<nint, nint, nint, int> converter);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IgnoreUnknownEvent(nint display, nint convertedEvent, nint wireEvent)
    {
        *(int*)convertedEvent = 0;
        return 0;
    }
}
