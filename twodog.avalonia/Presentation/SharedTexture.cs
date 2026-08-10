using System;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Godot;

namespace twodog.Presentation;

/// <summary>Outcome of <see cref="ISharedTexture.Acquire"/>.</summary>
internal enum AcquireResult
{
    /// <summary>The writer owns the texture; copy, then <see cref="ISharedTexture.Release"/>.</summary>
    Acquired,

    /// <summary>Still contended (compositor mid-read); skip this frame and retry later.</summary>
    Busy,

    /// <summary>Terminal (device removed, abandoned mutex); the presenter should fail over.</summary>
    Failed,
}

/// <summary>
/// One shareable render target: an engine-side copy target (the RID handed to
/// <c>external_texture_present</c>) plus the platform handle Avalonia's compositor imports.
/// All calls happen on the UI thread.
/// </summary>
internal interface ISharedTexture : IDisposable
{
    int Width { get; }
    int Height { get; }
    Rid Rid { get; }
    IPlatformHandle Handle { get; }
    PlatformGraphicsExternalImageProperties ImportProperties { get; }

    /// <summary>Before the engine copy; only <see cref="AcquireResult.Acquired"/> permits the
    /// copy and requires a matching <see cref="Release"/>.</summary>
    AcquireResult Acquire();

    /// <summary>After the engine copy completed (the present call stalls until it has).
    /// False is a terminal sync failure; the presenter should fail over.</summary>
    bool Release();

    /// <summary>Queues the compositor-side present of this texture's imported image on the
    /// surface, engaging whatever synchronization flavor the texture implements.</summary>
    Task PresentAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image);
}

/// <summary>Creates the platform's shared-texture flavor; owns any device state shared
/// between recreations (resizes).</summary>
internal interface ISharedTextureFactory : IDisposable
{
    /// <summary>The <c>KnownPlatformGraphicsExternalImageHandleTypes</c> value the compositor
    /// must support for this factory's textures.</summary>
    string AvaloniaHandleType { get; }

    ISharedTexture Create(RenderingDevice rd, int width, int height);
}

/// <summary>
/// Export-style sharing (Linux Vulkan opaque fds, macOS IOSurfaces): the engine allocates
/// the shareable texture and this side hands its exported handle to the compositor. The
/// exported fd's ownership transfers to the compositor on import; an IOSurfaceRef stays
/// valid while the RID lives (the compositor retains it on import).
/// </summary>
internal sealed class EngineExportedTextureFactory(
    RenderingDevice.ExternalTextureShareHandleType shareType,
    string avaloniaHandleType,
    bool needsMemorySize) : ISharedTextureFactory
{
    public string AvaloniaHandleType => avaloniaHandleType;

    public ISharedTexture Create(RenderingDevice rd, int width, int height)
    {
        var rid = rd.ExternalTextureCreate(shareType, RenderingDevice.DataFormat.R8G8B8A8Unorm,
            (uint)width, (uint)height);
        if (!rid.IsValid)
            throw new NotSupportedException($"external texture creation failed ({shareType})");

        return new EngineExportedTexture(rd, rid, width, height, avaloniaHandleType,
            needsMemorySize ? rd.ExternalTextureGetMemorySize(rid) : 0);
    }

    public void Dispose()
    {
    }

    private sealed class EngineExportedTexture(
        RenderingDevice rd, Rid rid, int width, int height, string handleType, ulong memorySize)
        : ISharedTexture
    {
        public int Width => width;
        public int Height => height;
        public Rid Rid => rid;

        public IPlatformHandle Handle { get; } =
            new PlatformHandle((nint)rd.ExternalTextureGetHandle(rid), handleType);

        public PlatformGraphicsExternalImageProperties ImportProperties => new()
        {
            Width = width,
            Height = height,
            Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
            MemorySize = memorySize,
            // The engine writes rows top-down; without this GL-backed compositors assume a
            // bottom-left origin and show the viewport flipped.
            TopLeftOrigin = true,
        };

        public AcquireResult Acquire() => AcquireResult.Acquired;

        public bool Release() => true;

        // No explicit sync primitive: the engine's present stalls until the copy finished on
        // the GPU, and the import type is coherent (exported memory, IOSurface).
        public Task PresentAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image) =>
            surface.UpdateAsync(image);

        public void Dispose() => rd.FreeRid(rid);
    }
}
