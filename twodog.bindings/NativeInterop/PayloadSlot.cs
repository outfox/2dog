namespace Godot.NativeInterop;

/// <summary>
/// Pointer-sized opaque payload slots (String, StringName, packed string
/// array elements): one pointer in native memory, kept as ulong on the
/// managed side (high bytes zero). Reading or writing 8 bytes on a 32-bit
/// target (wasm32) would touch adjacent memory and break payload equality.
/// </summary>
public static unsafe class PayloadSlot
{
    /// <summary>Native size of one slot; also the element stride in packed string arrays.</summary>
    public static int Size => IntPtr.Size;

    public static ulong Read(nint slot) => IntPtr.Size == 8 ? *(ulong*)slot : *(uint*)slot;

    public static void Write(nint slot, ulong payload)
    {
        if (IntPtr.Size == 8) *(ulong*)slot = payload;
        else *(uint*)slot = (uint)payload;
    }
}
