using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace twodog;

/// <summary>Registers the existing .NET runtime before launching Godot's Android Activity.</summary>
/// <remarks>Experimental. Godot's Java host owns the engine lifecycle; do not call Engine.Start().</remarks>
[SupportedOSPlatform("android")]
public static class AndroidHost
{
    private static readonly object Sync = new();
    private static string? _registeredDirectory;

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    /// <summary>Registers GodotPlugins and the directory containing the game's extracted assembly.</summary>
    /// <param name="projectAssemblyDirectory">Private app directory containing the game DLL, extracted from APK assets.</param>
    public static void Register(string projectAssemblyDirectory)
    {
        if (!OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException("AndroidHost requires .NET for Android.");
        ArgumentException.ThrowIfNullOrWhiteSpace(projectAssemblyDirectory);
        var directory = Path.GetFullPath(projectAssemblyDirectory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        lock (Sync)
        {
            if (_registeredDirectory is not null)
            {
                if (_registeredDirectory != directory)
                    throw new InvalidOperationException("AndroidHost is already registered with another game directory.");
                return;
            }
            var module = NativeLibrary.Load("libgodot_android.so", typeof(AndroidHost).Assembly, null);
            // Native getenv() does not necessarily see Environment.SetEnvironmentVariable().
            if (setenv("GODOT_PROJECT_ASSEMBLY_DIR", directory, 1) != 0)
                throw new InvalidOperationException($"setenv failed: {Marshal.GetLastPInvokeError()}");
            HostedGodotPlugins.Register(module);
            _registeredDirectory = directory;
        }
    }
}
