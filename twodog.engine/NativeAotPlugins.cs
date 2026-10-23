using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;

namespace twodog;

/// <summary>
/// NativeAOT and single-file stand-in for GodotPlugins.Main: one image or bundle holds every assembly, so there is no
/// GodotPlugins.dll to load and no project assembly file for GDMono to hand back. Script assemblies are registered
/// during initialization.
/// </summary>
internal static unsafe class NativeAotPlugins
{
    // Layout of GodotPlugins.Main.PluginsCallbacks, filled for GDMono's hosted (LIBGODOT_HOSTFXR) initializer.
    [StructLayout(LayoutKind.Sequential)]
    internal struct PluginsCallbacks
    {
        public delegate* unmanaged<char*, godot_string*, godot_bool> LoadProjectAssemblyCallback;
        public delegate* unmanaged<char*, nint, int, nint> LoadToolsAssemblyCallback;
        public delegate* unmanaged<godot_bool> UnloadProjectPluginCallback;
    }

    private static bool _initialized;

    internal static nint InitializeFromEngineFunction =>
        (nint)(delegate* unmanaged<nint, godot_bool, PluginsCallbacks*, ManagedCallbacks*, nint, int, godot_bool>)
        &InitializeFromEngine;

    [UnmanagedCallersOnly]
    private static godot_bool InitializeFromEngine(nint godotDllHandle, godot_bool editorHint,
        PluginsCallbacks* pluginsCallbacks, ManagedCallbacks* managedCallbacks,
        nint unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        try
        {
            if (editorHint.ToBool())
                throw new PlatformNotSupportedException(
                    "TwoDog: the editor variant loads GodotTools at runtime, which NativeAOT cannot do.");

            var reinitializing = _initialized;
            if (!reinitializing)
            {
                NativeLibrary.SetDllImportResolver(typeof(GodotObject).Assembly,
                    new GodotDllImportResolver(godotDllHandle).OnResolveDllImport);
                AlcReloadCfg.Configure(alcReloadEnabled: false);
            }

            NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
            if (reinitializing)
                ScriptManagerBridge.ResetForEngineReinitialization();
            else
                LookupScriptAssemblies();
            _initialized = true;

            *pluginsCallbacks = new()
            {
                LoadProjectAssemblyCallback = &LoadProjectAssembly,
                LoadToolsAssemblyCallback = &LoadToolsAssembly,
                UnloadProjectPluginCallback = &UnloadProjectPlugin,
            };
            *managedCallbacks = ManagedCallbacks.Create();
            return godot_bool.True;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return godot_bool.False;
        }
    }

    // The source generators mark the assemblies holding scripts. NativeAOT loads every compiled assembly up front; the
    // JIT loads the game assembly (named by 2dog.engine.targets) only once a host touches one of its types.
    private static void LookupScriptAssemblies()
    {
        if (Assembly.GetEntryAssembly() is { } entry)
        {
            foreach (var name in AssemblyMetadata.Values(entry, "TwoDogGameAssembly"))
                Assembly.Load(name);
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDefined(typeof(AssemblyHasScriptsAttribute), inherit: false))
                ScriptManagerBridge.LookupScriptsInAssembly(assembly);
        }
    }

    // Only reached when a stale <game>.dll sits next to the executable; its scripts are already registered.
    [UnmanagedCallersOnly]
    private static godot_bool LoadProjectAssembly(char* assemblyPath, godot_string* outLoadedAssemblyPath)
    {
        *outLoadedAssemblyPath = Marshaling.ConvertStringToNative(new string(assemblyPath));
        return godot_bool.True;
    }

    [UnmanagedCallersOnly]
    private static nint LoadToolsAssembly(char* assemblyPath, nint unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        Console.Error.WriteLine("TwoDog: GodotTools cannot load into a NativeAOT host.");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static godot_bool UnloadProjectPlugin() => godot_bool.False;
}
