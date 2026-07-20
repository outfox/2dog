using System.Runtime.InteropServices;
using Godot.NativeInterop;

namespace Godot;

/// <summary>
/// Godot Callable (16-byte opaque). Two flavors:
/// - bound: new Callable(target, "method_name")
/// - custom: Callable.From(() => ...) wrapping a C# delegate via
///   callable_custom_create2. The delegate's GCHandle is released by the
///   engine's free_func when the LAST engine-side copy of the callable dies
///   (signal disconnections included) - the engine refcount is the delegate
///   lifetime.
/// </summary>
public sealed unsafe class Callable : IDisposable
{
    internal Opaque16 Native;

    internal Callable(Opaque16 adopt) => Native = adopt;

    public Callable(GodotObject target, string method)
    {
        var obj = target.NativePtr;
        var sn = StringNames.Get(method).Opaque;
        Opaque16 native = default;
        var args = stackalloc nint[2];
        args[0] = (nint)(&obj);
        args[1] = (nint)(&sn);
        Builtins.Constructor(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_CALLABLE, 2)((nint)(&native), args);
        Native = native;
    }

    public static Callable From(Action action) => CreateCustom(action);
    public static Callable From(Action<Variant> action) => CreateCustom(action);
    public static Callable From(Action<Variant, Variant> action) => CreateCustom(action);
    public static Callable From(Action<Variant, Variant, Variant> action) => CreateCustom(action);

    private static Callable CreateCustom(Delegate action)
    {
        var info = new GDExtensionCallableCustomInfo2
        {
            callable_userdata = GCHandle.ToIntPtr(GCHandle.Alloc(action)),
            token = GdExtensionHost.Library,
            object_id = 0,
            call_func = (nint)(delegate* unmanaged<nint, nint*, long, nint, GDExtensionCallError*, void>)&CallFunc,
            free_func = (nint)(delegate* unmanaged<nint, void>)&FreeFunc,
            // hash/equal/less_than/to_string/is_valid left null: engine defaults.
        };

        Opaque16 native = default;
        GdExtensionInterface.CallableCustomCreate2((nint)(&native), (nint)(&info));
        return new Callable(native);
    }

    [UnmanagedCallersOnly]
    private static void CallFunc(nint userdata, nint* args, long argCount, nint ret, GDExtensionCallError* error)
    {
        *error = default; // GDEXTENSION_CALL_OK
        try
        {
            switch (GCHandle.FromIntPtr(userdata).Target)
            {
                case Action a:
                    a();
                    break;
                case Action<Variant> a when argCount >= 1:
                    a(BorrowedArg(args, 0));
                    break;
                case Action<Variant, Variant> a when argCount >= 2:
                    a(BorrowedArg(args, 0), BorrowedArg(args, 1));
                    break;
                case Action<Variant, Variant, Variant> a when argCount >= 3:
                    a(BorrowedArg(args, 0), BorrowedArg(args, 1), BorrowedArg(args, 2));
                    break;
                default:
                    error->error = GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_TOO_FEW_ARGUMENTS;
                    error->expected = 1;
                    break;
            }
        }
        catch (Exception e)
        {
            // Never let a managed exception cross the unmanaged boundary.
            Console.Error.WriteLine($"twodog.bindings: unhandled exception in Callable: {e}");
            error->error = GDExtensionCallErrorType.GDEXTENSION_CALL_ERROR_INVALID_METHOD;
        }
    }

    /// <summary>Hands the delegate an OWNED copy of a borrowed engine variant.</summary>
    private static Variant BorrowedArg(nint* args, int index) =>
        new(Variants.NewCopy(in *(NativeVariant*)args[index]));

    [UnmanagedCallersOnly]
    private static void FreeFunc(nint userdata) => GCHandle.FromIntPtr(userdata).Free();

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~Callable() => Release();

    private void Release()
    {
        var native = Native;
        if (native.A == 0 && native.B == 0) return;
        Native = default;
        Builtins.Destructor(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_CALLABLE)((nint)(&native));
    }
}

/// <summary>Godot Signal (16-byte opaque handle). Minimal wrap for now.</summary>
public sealed unsafe class Signal : IDisposable
{
    internal Opaque16 Native;

    internal Signal(Opaque16 adopt) => Native = adopt;

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~Signal() => Release();

    private void Release()
    {
        var native = Native;
        if (native.A == 0 && native.B == 0) return;
        Native = default;
        Builtins.Destructor(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_SIGNAL)((nint)(&native));
    }
}
