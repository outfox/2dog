using System;
using System.Diagnostics;
using System.Linq;
using Godot;
using Game;

namespace DriverCS;

internal static class Program
{
    private static unsafe int Main(string[] args)
    {
        Console.WriteLine("Starting Godot instance...");
        Console.WriteLine($"Arguments\n\t{args.Join()}");

        // Prepare arguments for Godot (editor mode!)
        string[] godotArgs =
        [
            "engine", //TODO: executable name, could actually be the PCK name in published build?
            "--path", "project"
        ];

        // Create Godot instance via P/Invoke (without starting)
        IntPtr instancePtr = LibGodot.libgodot_create_godot_instance(
            godotArgs.Length,
            godotArgs,
            &LibGodot.InitCallback
        );

        if (instancePtr == IntPtr.Zero)
        {
            Console.Error.WriteLine("Error creating Godot instance");
            return 1;
        }

        Console.WriteLine("Godot instance created successfully!");

        // Call start() using our minimal binding
        if (!LibGodot.CallGodotInstanceStart(instancePtr))
        {
            Console.Error.WriteLine("Error starting Godot instance");
            LibGodot.libgodot_destroy_godot_instance(instancePtr);
            return 1;
        }

        // Get the GodotInstance object from the native pointer
        GodotInstance? godotInstance = LibGodot.GetGodotInstanceFromPtr(instancePtr);
        if (godotInstance == null)
        {
            Console.Error.WriteLine("Failed to get GodotInstance from pointer");
            LibGodot.libgodot_destroy_godot_instance(instancePtr);
            return 1;
        }

        Console.WriteLine("Godot started successfully!");

        // Get the SceneTree
        MainLoop? mainLoop = Engine.Singleton.GetMainLoop();
        SceneTree? tree = mainLoop as SceneTree;
        if (tree == null)
        {
            Console.Error.WriteLine("Failed to get SceneTree");
            LibGodot.libgodot_destroy_godot_instance(instancePtr);
            return 1;
        }

        // Get the current scene
        Node? currentScene = tree.CurrentScene;
        if (currentScene == null)
        {
            Console.Error.WriteLine("No current scene loaded");
            LibGodot.libgodot_destroy_godot_instance(instancePtr);
            return 1;
        }

        // Find the TargetLabel node
        Label? targetLabel = currentScene.GetNode<Label>("TargetLabel");
        if (targetLabel == null)
        {
            Console.Error.WriteLine("TargetLabel not found in scene");
            LibGodot.libgodot_destroy_godot_instance(instancePtr);
            return 1;
        }
        
        // Find the TIcker node
        // This is here to demonstrate that we can access C# stuff properly
        Ticker? ticker = currentScene.GetNode<Ticker>("Ticker");
        if (ticker == null)
        {
            Console.Error.WriteLine("Ticker not found in scene");
            LibGodot.libgodot_destroy_godot_instance(instancePtr);
            return 1;
        }

        // Run for 10 seconds, updating the label text each frame
        Stopwatch stopwatch = Stopwatch.StartNew();
        int frameCount = 0;

        var device = RenderingServer.GetRenderingDevice();
        Console.WriteLine("device: " + device.GetInstanceId());
        while (!godotInstance.Iteration() && stopwatch.Elapsed.TotalSeconds < 200)
        {
            frameCount++;

            string match;
            if (ticker.localAccumulator == frameCount && Ticker.staticAccumulator == frameCount)
            {
                match = "(matches!)";
            }
            else
            {
                match = "(does not match! something is broken)";
            }
            targetLabel.Text = $"Frame: {frameCount} - Ticker values {ticker.localAccumulator} and {Ticker.staticAccumulator} {match}";
        }

        Console.WriteLine("Godot running complete.");

        // Clean up
        LibGodot.libgodot_destroy_godot_instance(instancePtr);
        Console.WriteLine("Godot instance destroyed.");

        return 0;
    }
}
