using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;

// ReSharper disable InconsistentNaming
// ReSharper disable once IdentifierTypo

namespace twodog;

internal enum GDExtensionInitializationLevel
{
    GDEXTENSION_INITIALIZATION_CORE,
    GDEXTENSION_INITIALIZATION_SERVERS,
    GDEXTENSION_INITIALIZATION_SCENE,
    GDEXTENSION_INITIALIZATION_EDITOR,
    GDEXTENSION_MAX_INITIALIZATION_LEVEL,
}

[StructLayout(LayoutKind.Sequential)]
internal struct GDExtensionInitialization
{
    public GDExtensionInitializationLevel minimum_initialization_level;
    public nint userdata;
    public nint initialize;
    public nint deinitialize;
}

internal static unsafe partial class LibGodot
{
    // Logical name only: LibGodotLoader's DllImportResolver maps it to the
    // variant-specific file (libgodot-release / libgodot-debug / libgodot-editor).
    internal const string LIBGODOT_LIBRARY_NAME = "libgodot";

    static LibGodot() => LibGodotLoader.Register();

    // StringName size (from godot-cpp)
    private const int STRING_NAME_SIZE = 8;

    // NOTE: All interop below deliberately uses the platform-default unmanaged
    // calling convention (no [Cdecl] modopts / UnmanagedCallConv) and blittable
    // parameter types only. On desktop x64 the default IS cdecl, and on
    // browser-wasm both modopt-annotated signatures and non-blittable
    // marshalling (e.g. string[] as LPArray) break the build-time
    // pinvoke/trampoline generation.

    // GDExtension interface function pointers
    private static delegate* unmanaged<nint, ulong> objectGetInstanceId;
    private static delegate* unmanaged<nint, nint, long, nint> classdbGetMethodBind;
    private static delegate* unmanaged<nint, nint, nint, nint, void> objectMethodBindPtrcall;
    private static delegate* unmanaged<nint, nint, byte, void> stringNameNewWithLatin1Chars;

    // Cache for the GodotInstance::start() method bind
    private static nint startMethodBind;

    [LibraryImport(LIBGODOT_LIBRARY_NAME)]
    public static partial nint libgodot_create_godot_instance(int p_argc, nint* p_argv, nint p_init_func);

    [LibraryImport(LIBGODOT_LIBRARY_NAME)]
    public static partial void libgodot_destroy_godot_instance(nint p_godot_instance);

    [UnmanagedCallersOnly]
    private static void InitializeCallback(nint userdata, GDExtensionInitializationLevel level)
    {
    }

    [UnmanagedCallersOnly]
    private static void DeinitializeCallback(nint userdata, GDExtensionInitializationLevel level)
    {
    }

    [UnmanagedCallersOnly]
    public static byte InitCallback(nint p_get_proc_address, nint p_library, GDExtensionInitialization* r_initialization)
    {
        r_initialization->minimum_initialization_level = GDExtensionInitializationLevel.GDEXTENSION_INITIALIZATION_CORE;
        r_initialization->initialize = (nint) (delegate* unmanaged<nint, GDExtensionInitializationLevel, void>) &InitializeCallback;
        r_initialization->deinitialize = (nint) (delegate* unmanaged<nint, GDExtensionInitializationLevel, void>) &DeinitializeCallback;

        // Load the GDExtension interface functions we need
        var getProcAddress = (delegate* unmanaged<nint, nint>) p_get_proc_address;

        // Load object_get_instance_id
        objectGetInstanceId = (delegate* unmanaged<nint, ulong>) GetProcAddress(getProcAddress, "object_get_instance_id"u8);

        // Load classdb_get_method_bind
        classdbGetMethodBind = (delegate* unmanaged<nint, nint, long, nint>) GetProcAddress(getProcAddress, "classdb_get_method_bind"u8);

        // Load object_method_bind_ptrcall
        objectMethodBindPtrcall = (delegate* unmanaged<nint, nint, nint, nint, void>) GetProcAddress(getProcAddress, "object_method_bind_ptrcall"u8);

        // Load string_name_new_with_latin1_chars
        stringNameNewWithLatin1Chars = (delegate* unmanaged<nint, nint, byte, void>) GetProcAddress(getProcAddress, "string_name_new_with_latin1_chars"u8);

        // Bind GodotInstance::start() method while we have all the necessary functions loaded
        if (stringNameNewWithLatin1Chars != null && classdbGetMethodBind != null)
        {
            Span<byte> classNameStorage = stackalloc byte[STRING_NAME_SIZE];
            Span<byte> methodNameStorage = stackalloc byte[STRING_NAME_SIZE];

            fixed (byte* pClassName = classNameStorage)
            fixed (byte* pMethodName = methodNameStorage)
            {
                // Create StringName objects
                fixed (byte* classNameStr = "GodotInstance"u8)
                fixed (byte* methodNameStr = "start"u8)
                {
                    stringNameNewWithLatin1Chars((nint) pClassName, (nint) classNameStr, 0); // 0 = not static
                    stringNameNewWithLatin1Chars((nint) pMethodName, (nint) methodNameStr, 0);

                    // Get the method bind for GodotInstance::start()
                    // Hash 2240911060 is the signature hash for: bool method() with no parameters
                    startMethodBind = classdbGetMethodBind((nint) pClassName, (nint) pMethodName, 2240911060);
                }
            }
        }

        return 1; // true
    }

    private static nint GetProcAddress(delegate* unmanaged<nint, nint> getProcAddress, ReadOnlySpan<byte> name)
    {
        // Ensure null termination
        Span<byte> buffer = stackalloc byte[name.Length + 1];
        name.CopyTo(buffer);
        buffer[name.Length] = 0;

        fixed (byte* pName = buffer)
        {
            return getProcAddress((nint) pName);
        }
    }

    // Minimal binding for GodotInstance::start()
    public static bool CallGodotInstanceStart(nint godotInstancePtr)
    {
        if (objectMethodBindPtrcall == null) throw new InvalidOperationException("GDExtension interface functions not loaded");

        if (startMethodBind == 0) throw new InvalidOperationException("GodotInstance::start() method bind not initialized");

        // Call the method using the raw object pointer
        Span<byte> returnValue = stackalloc byte[1];
        fixed (byte* retPtr = returnValue)
        {
            objectMethodBindPtrcall(startMethodBind, godotInstancePtr, 0, (nint) retPtr);
        }

        return returnValue[0] != 0;
    }

    // Helper to get GodotInstance from pointer
    public static GodotInstance? GetGodotInstanceFromPtr(nint godotInstancePtr)
    {
        if (objectGetInstanceId == null) throw new InvalidOperationException("GDExtension interface functions not loaded");

        // Get the instance ID from the pointer
        var instanceId = objectGetInstanceId(godotInstancePtr);
        if (instanceId == 0) return null;

        // Use Godot's internal API to get the managed object from the instance ID
        var obj = GodotObject.InstanceFromId(instanceId);
        return obj as GodotInstance;
    }
}