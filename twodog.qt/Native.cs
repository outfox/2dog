using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace twodog;

// Mirrors TwoDogQtInput in native/godotview.h.
[StructLayout(LayoutKind.Sequential)]
internal struct QtInput
{
    public QtInputType Type;
    public int Button;
    public int Buttons;
    public int Modifiers;
    public double X;
    public double Y;
    public double DeltaX;
    public double DeltaY;
    public int Key;
    public int Unicode;
    public int AutoRepeat;
    public uint ScanCode;
}

internal enum QtInputType
{
    MouseMove,
    MousePress,
    MouseRelease,
    MouseDoubleClick,
    Wheel,
    KeyPress,
    KeyRelease,
    FocusOut,
}

// Mirrors TwoDogQtCallbacks in native/godotview.h.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QtCallbacks
{
    public delegate* unmanaged<nint, void> Attached;
    public delegate* unmanaged<nint, void> Detached;
    public delegate* unmanaged<nint, byte> Frame;
    public delegate* unmanaged<void> Tick;
    public delegate* unmanaged<nint, QtInput*, void> Input;
    public delegate* unmanaged<void> Shutdown;
}

/// <summary>The C exports of the TwoDog QML module, which the package's build targets place next to the app.</summary>
internal static unsafe partial class Native
{
    private const string Library = "twodogqt";

    static Native() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);

    // QML loads the module from <app>/TwoDog; loading the same file here shares that instance.
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        var directory = Path.Combine(AppContext.BaseDirectory, "TwoDog");
        foreach (var file in (ReadOnlySpan<string>)["twodogqt.dll", "libtwodogqt.so", "libtwodogqt.dylib"])
        {
            if (NativeLibrary.TryLoad(Path.Combine(directory, file), out var handle)) return handle;
        }
        throw new DllNotFoundException(
            $"2dog.qt: the TwoDog QML module was not found in '{directory}'. Build the app with the 2dog.qt package " +
            "and a Qt Bridge for C# package so the module is compiled and copied next to it.");
    }

    [LibraryImport(Library, EntryPoint = "twodog_qt_set_callbacks")]
    public static partial void SetCallbacks(QtCallbacks* callbacks);

    [LibraryImport(Library, EntryPoint = "twodog_qt_view_pixel_size")]
    public static partial void ViewPixelSize(nint view, out int width, out int height);

    [LibraryImport(Library, EntryPoint = "twodog_qt_view_exposed")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool ViewExposed(nint view);

    /// <summary>The scene graph's QSGRendererInterface::GraphicsApi, or -1 before it is initialized.</summary>
    [LibraryImport(Library, EntryPoint = "twodog_qt_view_graphics_api")]
    public static partial int ViewGraphicsApi(nint view, out ulong adapterLuid);

    [LibraryImport(Library, EntryPoint = "twodog_qt_view_present")]
    public static partial void ViewPresent(nint view, byte* rgba, int width, int height);

    [LibraryImport(Library, EntryPoint = "twodog_qt_view_set_cursor")]
    public static partial void ViewSetCursor(nint view, int shape);

    /// <summary>Creates the shared textures of one size; returns their count, or 0 when Qt cannot share textures.</summary>
    [LibraryImport(Library, EntryPoint = "twodog_qt_gpu_create")]
    public static partial int GpuCreate(nint view, int width, int height, [MarshalAs(UnmanagedType.U1)] bool allowNtHandles,
        [MarshalAs(UnmanagedType.U1)] bool opaque, ulong* handles, [MarshalAs(UnmanagedType.U1)] out bool ntHandles);

    /// <summary>A buffer index the engine may write now, -1 while Qt still uses every buffer, -2 after a failure.</summary>
    [LibraryImport(Library, EntryPoint = "twodog_qt_gpu_begin_write")]
    public static partial int GpuBeginWrite(nint view);

    [LibraryImport(Library, EntryPoint = "twodog_qt_gpu_end_write")]
    public static partial void GpuEndWrite(nint view, int index, [MarshalAs(UnmanagedType.U1)] bool written);

    [LibraryImport(Library, EntryPoint = "twodog_qt_gpu_destroy")]
    public static partial void GpuDestroy(nint view);

    [LibraryImport(Library, EntryPoint = "twodog_qt_gpu_failed")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool GpuFailed(nint view);

    [LibraryImport(Library, EntryPoint = "twodog_qt_post")]
    public static partial void Post(delegate* unmanaged<nint, void> callback, nint state);

    [LibraryImport(Library, EntryPoint = "twodog_qt_is_gui_thread")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IsGuiThread();

    [LibraryImport(Library, EntryPoint = "twodog_qt_quit")]
    public static partial void Quit(int code);
}
