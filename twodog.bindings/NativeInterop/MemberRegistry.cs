using System.Runtime.InteropServices;

namespace Godot.NativeInterop;

/// <summary>
/// Registers user-class members (properties, signals, accessor methods) with
/// ClassDB. Driven by generated __BindMembers methods (the [Export]/[Signal]
/// source generator); invoked by ClassRegistry during class registration.
///
/// Marshalling: engine->managed calls arrive as variants (call_func) or raw
/// type pointers (ptrcall_func, converted to variants via the from-type
/// constructors) and route through one managed invoker per method. The engine
/// deep-copies registration structs, so all native temporaries are stack-local;
/// only the MethodDef GCHandles (method_userdata) persist.
/// </summary>
public sealed unsafe class MemberRegistry
{
    private readonly ClassRegistry.ClassInfo _info;

    internal MemberRegistry(ClassRegistry.ClassInfo info) => _info = info;

    private const uint PropertyUsageDefault = 6; // STORAGE | EDITOR

    /// <summary>
    /// Registers an exported property: hidden get_/set_ accessor methods plus
    /// the ClassDB property routing through them. Engine Set/Get, the
    /// inspector, and scene serialization all flow through these.
    /// </summary>
    public void Property(string name, VariantType type,
        Func<GodotObject, Variant> getter, Action<GodotObject, Variant> setter,
        long hint = 0, string hintString = "")
    {
        var getterName = "get_" + name;
        var setterName = "set_" + name;

        RegisterMethod(getterName, type, [], [], (self, _, _) => getter(self));
        RegisterMethod(setterName, null, [type], [name], (self, args, _) =>
        {
            var value = SignalArg.VariantAt(args, 0);
            try
            {
                setter(self, value);
            }
            finally
            {
                value.Dispose();
            }
            return default;
        });

        var classSn = StringNames.Get(_info.ClassName).Opaque;
        var nameSn = StringNames.Get(name).Opaque;
        var getterSn = StringNames.Get(getterName).Opaque;
        var setterSn = StringNames.Get(setterName).Opaque;
        var emptySn = StringNames.Get("").Opaque;
        var hintStr = NativeString.Create(hintString);

        var info = new GDExtensionPropertyInfo
        {
            type = (GDExtensionVariantType)(long)type,
            name = (nint)(&nameSn),
            class_name = (nint)(&emptySn),
            hint = (uint)hint,
            hint_string = (nint)(&hintStr),
            usage = PropertyUsageDefault,
        };
        GdExtensionInterface.ClassdbRegisterExtensionClassProperty(
            GdExtensionHost.Library, (nint)(&classSn), (nint)(&info), (nint)(&setterSn), (nint)(&getterSn));
        NativeString.Destroy(ref hintStr);
    }

    /// <summary>Registers a signal with typed arguments.</summary>
    public void Signal(string name, params (string name, VariantType type)[] args)
    {
        var classSn = StringNames.Get(_info.ClassName).Opaque;
        var signalSn = StringNames.Get(name).Opaque;
        var emptySn = StringNames.Get("").Opaque;

        var count = args.Length;
        var infos = stackalloc GDExtensionPropertyInfo[Math.Max(count, 1)];
        var argSns = stackalloc ulong[Math.Max(count, 1)];
        var hintStrs = stackalloc ulong[Math.Max(count, 1)];
        for (var i = 0; i < count; i++)
        {
            argSns[i] = StringNames.Get(args[i].name).Opaque;
            hintStrs[i] = NativeString.Create("");
            infos[i] = new GDExtensionPropertyInfo
            {
                type = (GDExtensionVariantType)(long)args[i].type,
                name = (nint)(argSns + i),
                class_name = (nint)(&emptySn),
                hint = 0,
                hint_string = (nint)(hintStrs + i),
                usage = PropertyUsageDefault,
            };
        }

        GdExtensionInterface.ClassdbRegisterExtensionClassSignal(
            GdExtensionHost.Library, (nint)(&classSn), (nint)(&signalSn), (nint)infos, count);

        for (var i = 0; i < count; i++)
        {
            NativeString.Destroy(ref hintStrs[i]);
        }
    }

    // ------------------------------------------------- method machinery --

    internal sealed class MethodDef
    {
        public required Func<GodotObject, nint, long, Variant> Invoke;
        public required VariantType[] ArgTypes;
        public required VariantType? Ret;
    }

    // method_userdata handles live as long as the registered class (forever).
    private static readonly List<GCHandle> KeepAlive = [];

    private void RegisterMethod(string name, VariantType? ret, VariantType[] argTypes, string[] argNames,
        Func<GodotObject, nint, long, Variant> invoke)
    {
        var def = new MethodDef { Invoke = invoke, ArgTypes = argTypes, Ret = ret };
        var handle = GCHandle.Alloc(def);
        lock (KeepAlive)
        {
            KeepAlive.Add(handle);
        }

        var classSn = StringNames.Get(_info.ClassName).Opaque;
        var nameSn = StringNames.Get(name).Opaque;
        var emptySn = StringNames.Get("").Opaque;

        var count = argTypes.Length;
        var argInfos = stackalloc GDExtensionPropertyInfo[Math.Max(count, 1)];
        var argSns = stackalloc ulong[Math.Max(count, 1)];
        var hintStrs = stackalloc ulong[Math.Max(count + 1, 1)];
        var metadata = stackalloc int[Math.Max(count, 1)]; // all METADATA_NONE
        for (var i = 0; i < count; i++)
        {
            argSns[i] = StringNames.Get(argNames[i]).Opaque;
            hintStrs[i] = NativeString.Create("");
            metadata[i] = 0;
            argInfos[i] = new GDExtensionPropertyInfo
            {
                type = (GDExtensionVariantType)(long)argTypes[i],
                name = (nint)(argSns + i),
                class_name = (nint)(&emptySn),
                hint = 0,
                hint_string = (nint)(hintStrs + i),
                usage = PropertyUsageDefault,
            };
        }

        GDExtensionPropertyInfo retInfo = default;
        var retSn = StringNames.Get("").Opaque;
        if (ret is { } retType)
        {
            hintStrs[count] = NativeString.Create("");
            retInfo = new GDExtensionPropertyInfo
            {
                type = (GDExtensionVariantType)(long)retType,
                name = (nint)(&retSn),
                class_name = (nint)(&emptySn),
                hint = 0,
                hint_string = (nint)(hintStrs + count),
                usage = PropertyUsageDefault,
            };
        }

        var mi = new GDExtensionClassMethodInfo
        {
            name = (nint)(&nameSn),
            method_userdata = GCHandle.ToIntPtr(handle),
            call_func = (nint)(delegate* unmanaged<nint, nint, nint*, long, nint, GDExtensionCallError*, void>)&CallFunc,
            ptrcall_func = (nint)(delegate* unmanaged<nint, nint, nint*, nint, void>)&PtrCallFunc,
            method_flags = 1, // GDEXTENSION_METHOD_FLAGS_DEFAULT
            has_return_value = ret is null ? (byte)0 : (byte)1,
            return_value_info = ret is null ? 0 : (nint)(&retInfo),
            return_value_metadata = 0,
            argument_count = (uint)count,
            arguments_info = count > 0 ? (nint)argInfos : 0,
            arguments_metadata = count > 0 ? (nint)metadata : 0,
            default_argument_count = 0,
            default_arguments = 0,
        };
        GdExtensionInterface.ClassdbRegisterExtensionClassMethod(GdExtensionHost.Library, (nint)(&classSn), (nint)(&mi));

        for (var i = 0; i < count; i++)
        {
            NativeString.Destroy(ref hintStrs[i]);
        }
        if (ret is not null) NativeString.Destroy(ref hintStrs[count]);
    }

    [UnmanagedCallersOnly]
    private static void CallFunc(nint userdata, nint instance, nint* args, long argc, nint ret, GDExtensionCallError* error)
    {
        *error = default;
        try
        {
            var def = (MethodDef)GCHandle.FromIntPtr(userdata).Target!;
            var self = (GodotObject)GCHandle.FromIntPtr(instance).Target!;
            var result = def.Invoke(self, (nint)args, argc);
            if (ret != 0)
            {
                // Engine hands an initialized variant slot: replace its contents.
                Variants.Destroy(ref *(NativeVariant*)ret);
                *(NativeVariant*)ret = result.Native;
            }
            else
            {
                result.Dispose();
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in bound method: {e}");
            error->error = GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_INVALID_METHOD;
        }
    }

    [UnmanagedCallersOnly]
    private static void PtrCallFunc(nint userdata, nint instance, nint* args, nint ret)
    {
        try
        {
            var def = (MethodDef)GCHandle.FromIntPtr(userdata).Target!;
            var self = (GodotObject)GCHandle.FromIntPtr(instance).Target!;

            // Synthesize a variant-arg block so one invoker serves both paths.
            var count = def.ArgTypes.Length;
            var natives = stackalloc NativeVariant[Math.Max(count, 1)];
            var ptrs = stackalloc nint[Math.Max(count, 1)];
            for (var i = 0; i < count; i++)
            {
                natives[i] = Variants.FromTypePointer((GDExtensionVariantType)(long)def.ArgTypes[i], args[i]);
                ptrs[i] = (nint)(natives + i);
            }

            var result = def.Invoke(self, (nint)ptrs, count);

            for (var i = 0; i < count; i++)
            {
                Variants.Destroy(ref natives[i]);
            }

            if (def.Ret is { } retType && ret != 0)
            {
                Variants.ToTypePointer((GDExtensionVariantType)(long)retType, in result.Native, ret);
            }
            result.Dispose();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in bound method (ptrcall): {e}");
        }
    }
}
