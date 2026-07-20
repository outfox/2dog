namespace Godot.NativeInterop;

/// <summary>
/// Raw refcount operations on RefCounted-derived engine objects.
/// Hashes from extension_api.json (4.7-stable). RefCounted is a core class,
/// so binds resolve any time after the extension entry point ran.
/// </summary>
public static unsafe class RefCountedNative
{
    private static nint _mbReference;
    private static nint _mbUnreference;
    private static nint _mbGetReferenceCount;

    private static nint MbReference => _mbReference != 0 ? _mbReference : _mbReference = MethodBinds.Resolve("RefCounted", "reference", 2240911060);
    private static nint MbUnreference => _mbUnreference != 0 ? _mbUnreference : _mbUnreference = MethodBinds.Resolve("RefCounted", "unreference", 2240911060);
    private static nint MbGetReferenceCount => _mbGetReferenceCount != 0 ? _mbGetReferenceCount : _mbGetReferenceCount = MethodBinds.Resolve("RefCounted", "get_reference_count", 3905245786);

    /// <summary>Increments the refcount. Returns false only on overflow/dead object.</summary>
    public static bool Reference(nint refCounted) => MethodBinds.CallRet<byte>(MbReference, refCounted) != 0;

    /// <summary>Decrements the refcount. Returns true when the object should die (count hit 0) - caller must destroy it.</summary>
    public static bool Unreference(nint refCounted) => MethodBinds.CallRet<byte>(MbUnreference, refCounted) != 0;

    public static long GetReferenceCount(nint refCounted) => MethodBinds.CallRet<long>(MbGetReferenceCount, refCounted);
}
