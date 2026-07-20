using Godot.NativeInterop;

namespace Godot.Collections;

/// <summary>
/// Godot's Variant array (8-byte opaque handle to refcounted COW storage).
/// Dispose (or let the finalizer) drop this handle's reference; builtin
/// destructors are atomic unrefs and safe from the finalizer thread.
/// Method hashes from extension_api.json builtin_classes.
/// </summary>
public sealed unsafe class Array : IDisposable
{
    internal ulong Native;
    private const GDExtensionVariantType T = GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_ARRAY;

    public Array()
    {
        ulong native = 0;
        Builtins.Constructor(T, 0)((nint)(&native), null);
        Native = native;
    }

    internal Array(ulong adopt) => Native = adopt;

    public int Count
    {
        get
        {
            long size = 0;
            fixed (ulong* self = &Native)
            {
                Builtins.Method(T, "size", 3173160232)((nint)self, null, (nint)(&size), 0);
            }
            return (int)size;
        }
    }

    public void Add(Variant value)
    {
        var args = stackalloc nint[1];
        args[0] = (nint)(&value.Native);
        fixed (ulong* self = &Native)
        {
            Builtins.Method(T, "push_back", 3316032543)((nint)self, args, 0, 1);
        }
    }

    public bool Contains(Variant value)
    {
        var args = stackalloc nint[1];
        args[0] = (nint)(&value.Native);
        byte ret = 0;
        fixed (ulong* self = &Native)
        {
            Builtins.Method(T, "has", 3680194679)((nint)self, args, (nint)(&ret), 1);
        }
        return ret != 0;
    }

    public void Clear()
    {
        fixed (ulong* self = &Native)
        {
            Builtins.Method(T, "clear", 3218959716)((nint)self, null, 0, 0);
        }
    }

    /// <summary>Gets an owned copy of the element / assigns into the slot.</summary>
    public Variant this[int index]
    {
        get
        {
            fixed (ulong* self = &Native)
            {
                var slot = (NativeVariant*)GdExtensionInterface.ArrayOperatorIndex((nint)self, index);
                if (slot == null) throw new IndexOutOfRangeException($"Array index {index} out of range (Count={Count}).");
                return new Variant(Variants.NewCopy(in *slot));
            }
        }
        set
        {
            fixed (ulong* self = &Native)
            {
                var slot = (NativeVariant*)GdExtensionInterface.ArrayOperatorIndex((nint)self, index);
                if (slot == null) throw new IndexOutOfRangeException($"Array index {index} out of range (Count={Count}).");
                Variants.Destroy(ref *slot);
                *slot = Variants.NewCopy(in value.Native);
            }
        }
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~Array() => Release();

    private void Release()
    {
        var native = Native;
        if (native == 0) return;
        Native = 0;
        Builtins.Destructor(T)((nint)(&native));
    }
}

/// <summary>Godot's Variant dictionary (8-byte opaque handle, refcounted COW).</summary>
public sealed unsafe class Dictionary : IDisposable
{
    internal ulong Native;
    private const GDExtensionVariantType T = GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_DICTIONARY;

    public Dictionary()
    {
        ulong native = 0;
        Builtins.Constructor(T, 0)((nint)(&native), null);
        Native = native;
    }

    internal Dictionary(ulong adopt) => Native = adopt;

    public int Count
    {
        get
        {
            long size = 0;
            fixed (ulong* self = &Native)
            {
                Builtins.Method(T, "size", 3173160232)((nint)self, null, (nint)(&size), 0);
            }
            return (int)size;
        }
    }

    public bool ContainsKey(Variant key)
    {
        var args = stackalloc nint[1];
        args[0] = (nint)(&key.Native);
        byte ret = 0;
        fixed (ulong* self = &Native)
        {
            Builtins.Method(T, "has", 3680194679)((nint)self, args, (nint)(&ret), 1);
        }
        return ret != 0;
    }

    public bool Remove(Variant key)
    {
        var args = stackalloc nint[1];
        args[0] = (nint)(&key.Native);
        byte ret = 0;
        fixed (ulong* self = &Native)
        {
            Builtins.Method(T, "erase", 1776646889)((nint)self, args, (nint)(&ret), 1);
        }
        return ret != 0;
    }

    public void Clear()
    {
        fixed (ulong* self = &Native)
        {
            Builtins.Method(T, "clear", 3218959716)((nint)self, null, 0, 0);
        }
    }

    /// <summary>
    /// Gets an owned copy of the value / assigns into the slot. Getter on a
    /// missing key inserts NIL (engine map semantics) - use ContainsKey first
    /// when that matters.
    /// </summary>
    public Variant this[Variant key]
    {
        get
        {
            fixed (ulong* self = &Native)
            {
                var slot = (NativeVariant*)GdExtensionInterface.DictionaryOperatorIndex((nint)self, (nint)(&key.Native));
                return new Variant(Variants.NewCopy(in *slot));
            }
        }
        set
        {
            fixed (ulong* self = &Native)
            {
                var slot = (NativeVariant*)GdExtensionInterface.DictionaryOperatorIndex((nint)self, (nint)(&key.Native));
                Variants.Destroy(ref *slot);
                *slot = Variants.NewCopy(in value.Native);
            }
        }
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    ~Dictionary() => Release();

    private void Release()
    {
        var native = Native;
        if (native == 0) return;
        Native = 0;
        Builtins.Destructor(T)((nint)(&native));
    }
}
