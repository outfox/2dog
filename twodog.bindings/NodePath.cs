using Godot.NativeInterop;

namespace Godot;

/// <summary>
/// Godot NodePath (8-byte opaque handle). Implicitly convertible from string,
/// which makes generated methods like Node.GetNode("Main") ergonomic.
/// </summary>
public sealed unsafe class NodePath : IDisposable
{
    internal ulong Native;
    private const GDExtensionVariantType T = GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_NODE_PATH;

    public NodePath(string path)
    {
        var str = NativeString.Create(path);
        ulong native = 0;
        var args = stackalloc nint[1];
        args[0] = (nint)(&str);
        Builtins.Constructor(T, 2)((nint)(&native), args); // ctor 2: NodePath(String)
        NativeString.Destroy(ref str);
        Native = native;
    }

    internal NodePath(ulong adopt) => Native = adopt;

    public static implicit operator NodePath(string path) => new(path);

    public override string ToString()
    {
        fixed (ulong* self = &Native)
        {
            var fromNp = (delegate* unmanaged<nint, nint, void>)GdExtensionInterface.GetVariantFromTypeConstructor((int)T);
            NativeVariant v;
            fromNp((nint)(&v), (nint)self);
            var result = Variants.ToManagedString(in v);
            Variants.Destroy(ref v);
            return result;
        }
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~NodePath() => Release();

    private void Release()
    {
        var native = Native;
        if (native == 0) return;
        Native = 0;
        Builtins.Destructor(T)((nint)(&native));
    }
}
