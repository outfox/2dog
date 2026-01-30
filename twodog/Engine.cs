using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Godot;

namespace twodog;

public class Engine(string project, string? path = null, params string[] args) : IDisposable
{
    private static IntPtr _godotInstancePtr = IntPtr.Zero;

    // .NET's Environment.SetEnvironmentVariable does not propagate to native getenv()
    // on Linux/.NET 8+. We must call setenv directly for Godot's native code to see it.
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    public SceneTree Tree => Godot.Engine.Singleton.GetMainLoop() as SceneTree ??
                             throw new NullReferenceException($"{nameof(Engine)}: Failed to get SceneTree.");


    public void Dispose()
    {
        if (_godotInstancePtr == IntPtr.Zero || _godotInstancePtr == IntPtr.MinValue) return;
        Destroy();
    }

    public GodotInstance Start()
    {
        if (_godotInstancePtr != IntPtr.Zero)
            throw new InvalidOperationException(
                $"{nameof(Engine)} Godot instance was previously created. This can be done only once per process (this is a Godot limitation).");

        // Ensure GODOTSHARP_DIR points to the directory containing GodotPlugins.dll.
        // When the host process is not in the output directory (e.g. dotnet test
        // uses /usr/share/dotnet/dotnet), Godot's exe_dir fallback won't work.
        // Must use native setenv on Unix because .NET's SetEnvironmentVariable
        // doesn't propagate to native getenv() on Linux/.NET 8+.
        {
            var assemblyDir = Path.GetDirectoryName(typeof(Engine).Assembly.Location);
            if (assemblyDir != null && File.Exists(Path.Combine(assemblyDir, "GodotPlugins.dll")))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    setenv("GODOTSHARP_DIR", assemblyDir, 1);
                else
                    System.Environment.SetEnvironmentVariable("GODOTSHARP_DIR", assemblyDir);
            }
        }

        Console.WriteLine("Starting Godot instance...");

        // Prepare arguments for Godot
        List<string> godotArgs = [project];
        if (!string.IsNullOrEmpty(path))
        {
            godotArgs.Add("--path");
            godotArgs.Add(path);
        }

        godotArgs.AddRange(args);

        // Create a Godot instance via P/Invoke (without starting)
        _godotInstancePtr = CreateGodotInstance(godotArgs.ToArray());

        if (_godotInstancePtr == IntPtr.Zero)
            throw new NullReferenceException($"{nameof(Engine)}: Error creating Godot instance, returned IntPtr.Zero");

        Console.WriteLine($"{nameof(Engine)}: Godot instance created successfully!");

        // Call start() using our minimal binding
        if (!LibGodot.CallGodotInstanceStart(_godotInstancePtr))
        {
            Console.Error.WriteLine("Error starting Godot instance");
            LibGodot.libgodot_destroy_godot_instance(_godotInstancePtr);
            throw new Exception($"{nameof(Engine)}: Error starting Godot instance");
        }

        // Get the GodotInstance object from the native pointer
        var godotInstance = LibGodot.GetGodotInstanceFromPtr(_godotInstancePtr);
        if (godotInstance == null)
        {
            Console.Error.WriteLine($"{nameof(Engine)}: Failed to get GodotInstance from pointer");
            LibGodot.libgodot_destroy_godot_instance(_godotInstancePtr);
            throw new NullReferenceException($"{nameof(Engine)}: Failed to get GodotInstance from pointer.");
        }

        Console.WriteLine($"{nameof(Engine)}: Godot started successfully!");
        return godotInstance;
    }

    private static void Destroy()
    {
        LibGodot.libgodot_destroy_godot_instance(_godotInstancePtr);
        Console.WriteLine($"{nameof(Engine)}: Godot instance destroyed.");
        _godotInstancePtr = IntPtr.MinValue;
    }

    private static unsafe IntPtr CreateGodotInstance(string[] args)
    {
        return LibGodot.libgodot_create_godot_instance(
            args.Length,
            args,
            &LibGodot.InitCallback
        );
    }
}