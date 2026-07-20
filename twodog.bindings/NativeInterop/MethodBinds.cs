namespace Godot.NativeInterop;

/// <summary>
/// Method-bind resolution and ptrcall helpers. Hashes come from
/// extension_api.json for the engine version in the godot submodule.
///
/// ClassDB timing: core classes (Object, Engine, ClassDB, GodotInstance, ...)
/// resolve from the entry point onward; scene classes (Node, SceneTree, ...)
/// only from SCENE-level initialization onward.
/// </summary>
public static unsafe class MethodBinds
{
    /// <summary>Resolves a method bind; returns 0 (with an engine-side error print) if unknown.</summary>
    public static nint Resolve(string className, string methodName, long hash)
    {
        var snClass = StringNames.Get(className).Opaque;
        var snMethod = StringNames.Get(methodName).Opaque;
        return GdExtensionInterface.ClassdbGetMethodBind((nint)(&snClass), (nint)(&snMethod), hash);
    }

    /// <summary>Ptrcall with no arguments and no return.</summary>
    public static void Call(nint methodBind, nint instance) =>
        GdExtensionInterface.ObjectMethodBindPtrcall(methodBind, instance, 0, 0);

    /// <summary>Ptrcall with pre-encoded argument pointers and an optional return slot.</summary>
    public static void Call(nint methodBind, nint instance, ReadOnlySpan<nint> args, void* ret = null)
    {
        fixed (nint* pArgs = args)
        {
            GdExtensionInterface.ObjectMethodBindPtrcall(methodBind, instance, (nint)pArgs, (nint)ret);
        }
    }

    /// <summary>Ptrcall with no arguments returning a primitive/pointer-sized value.</summary>
    public static T CallRet<T>(nint methodBind, nint instance) where T : unmanaged
    {
        T ret = default;
        GdExtensionInterface.ObjectMethodBindPtrcall(methodBind, instance, 0, (nint)(&ret));
        return ret;
    }
}
