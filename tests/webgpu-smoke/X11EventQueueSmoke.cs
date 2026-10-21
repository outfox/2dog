using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;

internal static unsafe class X11EventQueueSmoke
{
    public static void Run()
    {
        var display = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.DisplayHandle);
        var converter = XESetWireToEvent(display, 35, (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&IgnoreEvent);
        XESetWireToEvent(display, 35, converter);
        long* converted = stackalloc long[24]; // XEvent's native-long padding on Linux x64.
        byte* wire = stackalloc byte[32];
        *(int*)converted = 35;
        var convert = (delegate* unmanaged[Cdecl]<nint, nint, nint, int>)converter;
        if (convert(display, (nint)converted, (nint)wire) != 0 || *(int*)converted != 0)
            throw new Exception("Ignored X11 event retained cookie fields that XCloseDisplay could free.");
        Console.WriteLine("NATIVE_X11_IGNORED_EVENT_PASSED");
    }

    [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint XESetWireToEvent(nint display, int eventNumber, nint converter);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IgnoreEvent(nint display, nint convertedEvent, nint wireEvent)
    {
        *(int*)convertedEvent = 0;
        return 0;
    }
}
