using System.Runtime.InteropServices;

namespace Godot.NativeInterop;

/// <summary>
/// GDExtension script instances for C# scripts: one permanently-allocated
/// GDExtensionScriptInstanceInfo3 vtable (the engine stores the pointer),
/// instance_data = strong GCHandle on a State pairing the CSharpScript with the
/// managed object adopted onto the owner node. Unset callbacks are null - the
/// engine-side wrapper has safe fallbacks for every one of them.
/// </summary>
internal static unsafe class ScriptInstanceBridge
{
    internal sealed class State
    {
        public required CSharpScript Script;
        public required GodotObject Instance;
        public required ScriptMemberMap Members;
        public required nint Owner;
    }

    private static readonly nint Info = AllocInfo();

    private static nint AllocInfo()
    {
        var p = (GDExtensionScriptInstanceInfo3*)NativeMemory.AllocZeroed((nuint)sizeof(GDExtensionScriptInstanceInfo3));
        p->set_func = (nint)(delegate* unmanaged<nint, nint, nint, byte>)&SetFunc;
        p->get_func = (nint)(delegate* unmanaged<nint, nint, nint, byte>)&GetFunc;
        p->get_owner_func = (nint)(delegate* unmanaged<nint, nint>)&GetOwnerFunc;
        p->has_method_func = (nint)(delegate* unmanaged<nint, nint, byte>)&HasMethodFunc;
        p->call_func = (nint)(delegate* unmanaged<nint, nint, nint, long, nint, nint, void>)&CallFunc;
        p->notification_func = (nint)(delegate* unmanaged<nint, int, byte, void>)&NotificationFunc;
        p->to_string_func = (nint)(delegate* unmanaged<nint, nint, nint, void>)&ToStringFunc;
        p->get_script_func = (nint)(delegate* unmanaged<nint, nint>)&GetScriptFunc;
        p->get_language_func = (nint)(delegate* unmanaged<nint, nint>)&GetLanguageFunc;
        p->free_func = (nint)(delegate* unmanaged<nint, void>)&FreeFunc;
        return (nint)p;
    }

    /// <summary>
    /// Creates the managed instance adopted onto the existing owner object and
    /// wraps it in an engine script instance. Returns 0 on failure (the engine
    /// treats that as "no script instance", matching an uninstantiable script).
    /// </summary>
    internal static nint Create(CSharpScript script, nint owner)
    {
        var info = script.Target;
        if (info is null) return 0;

        GodotObject instance;
        ClassRegistry.PendingScriptOwner = owner;
        try
        {
            instance = (GodotObject)Activator.CreateInstance(info.Type)!;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog: constructing script instance {info.Type} failed: {e}");
            return 0;
        }
        finally
        {
            ClassRegistry.PendingScriptOwner = 0;
        }

        var state = new State
        {
            Script = script,
            Instance = instance,
            Members = ScriptMemberMap.For(info.Type),
            Owner = owner,
        };
        return GdExtensionInterface.ScriptInstanceCreate3(Info, GCHandle.ToIntPtr(GCHandle.Alloc(state)));
    }

    private static State? Resolve(nint data) => GCHandle.FromIntPtr(data).Target as State;

    [UnmanagedCallersOnly]
    private static byte SetFunc(nint data, nint nameSn, nint value)
    {
        try
        {
            if (Resolve(data) is not { } state) return 0;
            var name = StringNames.Read(PayloadSlot.Read(nameSn));
            if (!state.Members.Exports.TryGetValue(name, out var entry)) return 0;
            using var v = new Variant(Variants.NewCopy(in *(NativeVariant*)value));
            entry.Set(state.Instance, VariantMarshal.FromVariant(in v, entry.ClrType));
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog: script instance set failed: {e}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static byte GetFunc(nint data, nint nameSn, nint ret)
    {
        try
        {
            if (Resolve(data) is not { } state) return 0;
            var name = StringNames.Read(PayloadSlot.Read(nameSn));
            if (!state.Members.Exports.TryGetValue(name, out var entry)) return 0;
            var v = VariantMarshal.ToVariant(entry.Get(state.Instance), entry.ClrType);
            Variants.Destroy(ref *(NativeVariant*)ret);
            *(NativeVariant*)ret = v.Native; // move: engine owns the result
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog: script instance get failed: {e}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static nint GetOwnerFunc(nint data)
    {
        try { return Resolve(data)?.Owner ?? 0; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly]
    private static byte HasMethodFunc(nint data, nint nameSn)
    {
        try
        {
            if (Resolve(data) is not { } state) return 0;
            return state.Members.FindMethod(StringNames.Read(PayloadSlot.Read(nameSn))) is not null ? (byte)1 : (byte)0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog: script instance has_method failed: {e}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void CallFunc(nint data, nint methodSn, nint args, long argc, nint ret, nint err)
    {
        // The hot dispatch boundary (_ready/_process/... and GDScript calls into
        // C#). Contract: INVALID_METHOD when the script has no such method (the
        // GDVIRTUAL path relies on it to fall through), OK once user code ran -
        // user exceptions are logged, not propagated (GodotSharp-style).
        var e = (GDExtensionCallError*)err;
        try
        {
            if (Resolve(data) is not { } state)
            {
                e->error = GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_INSTANCE_IS_NULL;
                return;
            }
            var mi = state.Members.FindMethod(StringNames.Read(PayloadSlot.Read(methodSn)));
            if (mi is null)
            {
                e->error = GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_INVALID_METHOD;
                return;
            }

            var ps = mi.GetParameters();
            if (argc != ps.Length)
            {
                e->error = argc > ps.Length
                    ? GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_TOO_MANY_ARGUMENTS
                    : GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_TOO_FEW_ARGUMENTS;
                e->expected = ps.Length;
                return;
            }

            var managedArgs = ps.Length == 0 ? null : new object?[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                using var v = SignalArg.VariantAt(args, i);
                managedArgs![i] = VariantMarshal.FromVariant(in v, ps[i].ParameterType);
            }

            object? result;
            try
            {
                result = mi.Invoke(state.Instance, managedArgs);
            }
            catch (Exception userEx)
            {
                Console.Error.WriteLine($"twodog: unhandled exception in script method {mi.Name}: {userEx}");
                e->error = GDExtensionCallErrorType.GDEXTENSION_CALL_OK;
                return;
            }

            if (mi.ReturnType != typeof(void) && ret != 0)
            {
                var rv = VariantMarshal.ToVariant(result, mi.ReturnType);
                Variants.Destroy(ref *(NativeVariant*)ret);
                *(NativeVariant*)ret = rv.Native; // move
            }
            e->error = GDExtensionCallErrorType.GDEXTENSION_CALL_OK;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"twodog: script instance call failed: {ex}");
            e->error = GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_INVALID_METHOD;
        }
    }

    [UnmanagedCallersOnly]
    private static void NotificationFunc(nint data, int what, byte reversed)
    {
        try
        {
            Resolve(data)?.Instance._Notification(what);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog: unhandled exception in _Notification: {e}");
        }
    }

    [UnmanagedCallersOnly]
    private static void ToStringFunc(nint data, nint isValid, nint strRet)
    {
        // Explicit "not customized": the engine falls back to the class-name
        // default instead of reading an unset out-parameter.
        try { *(byte*)isValid = 0; }
        catch { /* never unwind into the engine */ }
    }

    [UnmanagedCallersOnly]
    private static nint GetScriptFunc(nint data)
    {
        try { return Resolve(data)?.Script.NativePtr ?? 0; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly]
    private static nint GetLanguageFunc(nint data)
    {
        try { return CSharpLanguage.Singleton?.NativePtr ?? 0; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly]
    private static void FreeFunc(nint data)
    {
        try
        {
            GCHandle.FromIntPtr(data).Free();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog: script instance free failed: {e}");
        }
    }
}
