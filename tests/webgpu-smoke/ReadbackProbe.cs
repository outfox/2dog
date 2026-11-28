using Godot;

/// <summary>Reads a buffer and a texture back through the rendering device, polling over several frames.</summary>
/// <remarks>
/// A browser cannot wait for the GPU, so results arrive a frame or more late. They must still be the bytes written
/// before the request, never zeros or the contents from before an earlier recorded write.
/// </remarks>
internal sealed class ReadbackProbe
{
    private static readonly byte[] Initial = Fill(1), Updated = Fill(101);

    private readonly RenderingDevice device = RenderingServer.GetRenderingDevice();
    private readonly Rid buffer, texture;
    private byte[]? asyncBuffer, asyncTexture;
    private byte[] syncBuffer = [];
    private int frames;

    public bool Passed { get; private set; }

    public ReadbackProbe()
    {
        buffer = device.StorageBufferCreate(16, Initial);
        // Recorded before the reads, so every read has to return the updated bytes.
        Check(device.BufferUpdate(buffer, 0, 16, Updated), "buffer update");
        Check(device.BufferGetDataAsync(buffer, Callable.From((byte[] data) => asyncBuffer = data)), "buffer request");
        syncBuffer = device.BufferGetData(buffer);

        using var format = new RDTextureFormat
        {
            Width = 2, Height = 2, Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
            UsageBits = RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
        };
        using var view = new RDTextureView();
        texture = device.TextureCreate(format, view, [Initial]);
        Check(device.TextureUpdate(texture, 0, Updated), "texture update");
        Check(device.TextureGetDataAsync(texture, 0, Callable.From((byte[] data) => asyncTexture = data)), "texture request");
    }

    /// <summary>Call once per frame until <see cref="Passed"/>; throws when a result is wrong or never arrives.</summary>
    public void Poll()
    {
        if (Passed) return;
        // The first result that arrives counts: a later one would hide a copy that ran before the update.
        if (syncBuffer.Length == 0) syncBuffer = device.BufferGetData(buffer);
        if (asyncBuffer is null || asyncTexture is null || syncBuffer.Length == 0)
        {
            if (++frames > 600)
                throw new Exception($"Readbacks did not arrive: async buffer {asyncBuffer is not null}, " +
                                    $"async texture {asyncTexture is not null}, buffer {syncBuffer.Length} bytes.");
            return;
        }
        Require(asyncBuffer, "asynchronous buffer");
        Require(asyncTexture, "asynchronous texture");
#if GODOT4_8_OR_GREATER
        // 4.7 copies a browser's synchronous readback ahead of recorded updates; outfox/godot#27 fixed it for 4.8.
        Require(syncBuffer, "buffer");
#endif
        device.FreeRid(buffer);
        device.FreeRid(texture);
        Passed = true;
    }

    private static byte[] Fill(byte first) => [.. Enumerable.Range(first, 16).Select(value => (byte)value)];

    private static void Check(Error error, string what)
    {
        if (error != Error.Ok) throw new Exception($"Readback probe: {what} returned {error}.");
    }

    private static void Require(byte[] actual, string what)
    {
        if (!actual.AsSpan().SequenceEqual(Updated))
            throw new Exception($"Readback probe: {what} readback returned [{string.Join(", ", actual)}].");
    }
}
