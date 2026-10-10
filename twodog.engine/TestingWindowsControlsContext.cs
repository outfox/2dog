using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace twodog.Testing;

// A DLL's application manifest does not affect testhost.exe. Activate comctl32 v6 on the
// engine's owner thread for the lifetime of the fixture, including native shutdown.
[SupportedOSPlatform("windows")]
internal sealed class WindowsControlsContext : IDisposable
{
    private readonly nint _handle;
    private readonly nuint _cookie;
    private bool _disposed;

    public WindowsControlsContext()
    {
        var manifest = Path.Combine(Path.GetTempPath(), $"2dog-controls-{Guid.NewGuid():N}.manifest");
        try
        {
            using (var source = typeof(WindowsControlsContext).Assembly.GetManifestResourceStream("twodog.Testing.Controls.manifest")
                ?? throw new InvalidOperationException("The 2dog common-controls manifest is missing."))
            using (var target = new FileStream(manifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                source.CopyTo(target);

            var context = new ActCtx { Size = (uint)Marshal.SizeOf<ActCtx>(), Source = manifest };
            _handle = CreateActCtxW(ref context);
            if (_handle == -1) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create Godot's common-controls context.");
            if (!ActivateActCtx(_handle, out _cookie))
            {
                var error = Marshal.GetLastWin32Error();
                ReleaseActCtx(_handle);
                throw new Win32Exception(error, "Cannot activate Godot's common-controls context.");
            }
        }
        finally { File.Delete(manifest); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!DeactivateActCtx(0, _cookie))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot deactivate Godot's common-controls context on its owner thread.");
        ReleaseActCtx(_handle);
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ActCtx
    {
        public uint Size;
        public uint Flags;
        public string Source;
        public ushort ProcessorArchitecture;
        public ushort LanguageId;
        public nint AssemblyDirectory;
        public nint ResourceName;
        public nint ApplicationName;
        public nint Module;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateActCtxW(ref ActCtx context);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ActivateActCtx(nint context, out nuint cookie);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeactivateActCtx(uint flags, nuint cookie);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern void ReleaseActCtx(nint context);
}
