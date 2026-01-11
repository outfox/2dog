using System;
using System.Collections.Generic;
using Godot;

namespace twodog;

public class Engine(string project, string? path = null, string[]? args = null) : IDisposable
{
    private static IntPtr _godotInstancePtr = IntPtr.Zero;

    public SceneTree SceneTree => Godot.Engine.Singleton.GetMainLoop() as SceneTree ??
                                        throw new NullReferenceException($"{nameof(Engine)}: Failed to get SceneTree.");

    public GodotInstance Start()
    {
        if (_godotInstancePtr != IntPtr.Zero)
            throw new InvalidOperationException(
                $"{nameof(Engine)} Godot instance was previously created. This can be done only once per process (this is a Godot limitation).");

        Console.WriteLine("Starting Godot instance...");

        // Prepare arguments for Godot (editor mode!)
        List<string> godotArgs = [ project ];
        if (!string.IsNullOrEmpty(path))
        {
            godotArgs.Add("--path");
            godotArgs.Add(path);
        }
        if (args != null)
        {
            godotArgs.AddRange(args);
        }   

        // Create Godot instance via P/Invoke (without starting)
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

    private static void Stop()
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


    public void Dispose()
    {
        if (_godotInstancePtr != IntPtr.Zero || _godotInstancePtr != IntPtr.MinValue) Stop();
    }

}