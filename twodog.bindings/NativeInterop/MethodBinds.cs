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

    private static nint _mbClassHasMethod;

    // Silent existence probe: extension_api.json is dumped from the editor
    // build, so template builds lack a few TOOLS-gated methods and the direct
    // get_method_bind lookup error-prints for each. ClassDB.class_has_method
    // gates those quietly; a true hash mismatch still prints (a real error).
    private static bool ClassHasMethod(ulong snClass, ulong snMethod)
    {
        if (_mbClassHasMethod == 0)
            _mbClassHasMethod = Resolve("ClassDB", "class_has_method", 3860701026);
        byte noInheritance = 0;
        var args = stackalloc nint[3] { (nint)(&snClass), (nint)(&snMethod), (nint)(&noInheritance) };
        byte ret = 0;
        GdExtensionInterface.ObjectMethodBindPtrcall(
            _mbClassHasMethod, InstanceBindings.GetSingletonPtr("ClassDB"), (nint)args, (nint)(&ret));
        return ret != 0;
    }

    /// <summary>
    /// Startup bulk resolution (GeneratedBinds): the method name is a transient
    /// owned StringName, not interned - resolving the full API surface must not
    /// permanently intern thousands of method names.
    /// </summary>
    public static nint ResolveBulk(string className, string methodName, long hash)
    {
        var snClass = StringNames.Get(className).Opaque;
        var snMethod = StringNames.CreateOwned(methodName);
        var mb = ClassHasMethod(snClass, snMethod)
            ? GdExtensionInterface.ClassdbGetMethodBind((nint)(&snClass), (nint)(&snMethod), hash)
            : 0;
        StringNames.DestroyOwned(ref snMethod);
        return mb;
    }

    /// <summary>
    /// Cold throw for generated call sites whose bind resolved to 0: the method
    /// is missing from the running engine build, or the engine has not reached
    /// SCENE initialization yet.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static void MissingThrow(string classDotMethod) =>
        throw new MissingMethodException(
            $"{classDotMethod} is not available (missing in this engine build, or the engine has not reached SCENE initialization).");

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
