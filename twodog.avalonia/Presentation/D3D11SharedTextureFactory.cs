using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Godot;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace twodog.Presentation;

/// <summary>
/// Import-style sharing for Windows: this side creates a D3D11 keyed-mutex shared texture on
/// the compositor's adapter, the engine imports its shared handle into Vulkan, and Avalonia
/// imports the same handle. NT handles are preferred (some drivers, e.g. 2020-era Intel,
/// import only those); legacy KMT global shared handles remain the fallback. The keyed mutex
/// is driven from the CPU around the engine's copy - writer acquires key 0 and releases
/// key 1; the compositor presents with (1, 0).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D11SharedTextureFactory : ISharedTextureFactory
{
    private const uint SharedResourceRead = 0x80000000;
    private const uint SharedResourceWrite = 0x00000001;

    private readonly D3D11 _api;
    private readonly bool _ntHandle;
    private ComPtr<ID3D11Device> _device;

    public string AvaloniaHandleType => _ntHandle
        ? KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle
        : KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle;

    public CompositionGpuImportedImageSynchronizationCapabilities RequiredSynchronization =>
        CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex;

    // The keyed mutex serializes the engine's writes against compositor reads on one texture.
    public int BufferCount => 1;

    /// <summary>Creates the device on the adapter with the given LUID (the compositor's device).</summary>
    public D3D11SharedTextureFactory(byte[]? adapterLuid, bool ntHandle)
    {
        _ntHandle = ntHandle;
        _api = D3D11.GetApi(null, false);
        try
        {
            using var adapter = FindAdapter(adapterLuid);

            ID3D11Device* device = null;
            // A specific adapter requires DriverType Unknown per D3D11CreateDevice rules. No immediate
            // context: this device only creates resources, and the keyed mutex synchronizes access.
            SilkMarshal.ThrowHResult(_api.CreateDevice(
                (IDXGIAdapter*)adapter.Handle,
                adapter.Handle is null ? D3DDriverType.Hardware : D3DDriverType.Unknown,
                0, (uint)CreateDeviceFlag.BgraSupport, null, 0, D3D11.SdkVersion,
                &device, null, (ID3D11DeviceContext**)null));
            _device = device;
        }
        catch
        {
            // A failed constructor cannot be disposed; the native-library context must not leak.
            _api.Dispose();
            throw;
        }
    }

    private static ComPtr<IDXGIAdapter1> FindAdapter(byte[]? luid)
    {
        if (luid is not { Length: 8 }) return default;
        var target = BitConverter.ToInt64(luid, 0);

        using var dxgi = DXGI.GetApi(null, false);
        IDXGIFactory1* factory = null;
        SilkMarshal.ThrowHResult(dxgi.CreateDXGIFactory1(SilkMarshal.GuidPtrOf<IDXGIFactory1>(), (void**)&factory));
        try
        {
            for (uint i = 0; ; i++)
            {
                IDXGIAdapter1* adapter = null;
                if (factory->EnumAdapters1(i, &adapter) != 0) break;
                AdapterDesc1 desc;
                adapter->GetDesc1(&desc);
                var adapterLuid = ((long)desc.AdapterLuid.High << 32) | (uint)desc.AdapterLuid.Low;
                if (adapterLuid == target) return adapter;
                adapter->Release();
            }
        }
        finally
        {
            factory->Release();
        }
        return default;
    }

    public ISharedTexture Create(RenderingDevice rd, int width, int height)
    {
        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc(1, 0),
            Usage = Usage.Default,
            BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
            MiscFlags = (uint)(_ntHandle
                ? ResourceMiscFlag.SharedKeyedmutex | ResourceMiscFlag.SharedNthandle
                : ResourceMiscFlag.SharedKeyedmutex),
        };
        ID3D11Texture2D* texture = null;
        SilkMarshal.ThrowHResult(_device.Get().CreateTexture2D(&desc, null, &texture));

        // Staged acquisition: any failure past this point must release what already exists,
        // or the CPU-fallback path leaks the texture and mutex on every attempt.
        IDXGIKeyedMutex* mutex = null;
        try
        {
            var mutexIid = IDXGIKeyedMutex.Guid;
            SilkMarshal.ThrowHResult(texture->QueryInterface(&mutexIid, (void**)&mutex));

            var shared = _ntHandle ? CreateNtHandle(texture) : GetKmtHandle(texture);
            try
            {
                var rid = rd.ExternalTextureCreate(
                    _ntHandle
                        ? RenderingDevice.ExternalTextureShareHandleType.D3D11NtKeyedMutex
                        : RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex,
                    RenderingDevice.DataFormat.R8G8B8A8Unorm,
                    (uint)width, (uint)height, (ulong)shared);
                if (!rid.IsValid)
                    throw new NotSupportedException("The engine could not import the D3D11 shared texture.");

                return new D3D11SharedTexture(rd, rid, texture, mutex, shared, width, height,
                    AvaloniaHandleType, ownsHandle: _ntHandle);
            }
            catch
            {
                // Neither the engine's Vulkan import nor Avalonia's takes NT handle
                // ownership; without this close each failed attempt leaks the handle.
                if (_ntHandle) CloseHandle(shared);
                throw;
            }
        }
        catch
        {
            if (mutex is not null) mutex->Release();
            texture->Release();
            throw;
        }
    }

    /// <summary>The legacy KMT global shared handle - not an owned resource.</summary>
    private static nint GetKmtHandle(ID3D11Texture2D* texture)
    {
        IDXGIResource* resource = null;
        var resourceIid = IDXGIResource.Guid;
        SilkMarshal.ThrowHResult(texture->QueryInterface(&resourceIid, (void**)&resource));
        try
        {
            void* handle = null;
            SilkMarshal.ThrowHResult(resource->GetSharedHandle(&handle));
            return (nint)handle;
        }
        finally
        {
            resource->Release();
        }
    }

    /// <summary>An owned NT handle the caller must eventually close.</summary>
    private static nint CreateNtHandle(ID3D11Texture2D* texture)
    {
        IDXGIResource1* resource = null;
        var resourceIid = IDXGIResource1.Guid;
        SilkMarshal.ThrowHResult(texture->QueryInterface(&resourceIid, (void**)&resource));
        try
        {
            void* handle = null;
            SilkMarshal.ThrowHResult(resource->CreateSharedHandle(
                null, SharedResourceRead | SharedResourceWrite, (char*)null, &handle));
            return (nint)handle;
        }
        finally
        {
            resource->Release();
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    public void Dispose()
    {
        _device.Dispose();
        _device = default;
        // Last: the API wrapper's native-library context must outlive objects created through it.
        _api.Dispose();
    }

    private sealed class D3D11SharedTexture(
        RenderingDevice rd, Rid rid, ID3D11Texture2D* texture, IDXGIKeyedMutex* mutex,
        nint sharedHandle, int width, int height, string handleType, bool ownsHandle) : ISharedTexture
    {
        private ComPtr<ID3D11Texture2D> _texture = texture;
        private ComPtr<IDXGIKeyedMutex> _mutex = mutex;

        public int Width => width;
        public int Height => height;
        public Rid Rid => rid;

        public IPlatformHandle Handle { get; } = new PlatformHandle(sharedHandle, handleType);

        public PlatformGraphicsExternalImageProperties ImportProperties => new()
        {
            Width = width,
            Height = height,
            Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
            // The engine writes rows top-down; without this the GL-backed compositor assumes
            // a bottom-left origin and shows the viewport flipped.
            TopLeftOrigin = true,
        };

        public AcquireResult Acquire()
        {
            if (_mutex.Handle is null) return AcquireResult.Failed;
            return _mutex.Get().AcquireSync(0, 0) switch
            {
                0 => AcquireResult.Acquired,
                // AcquireSync reports contention as WAIT_TIMEOUT (0x102, success severity; some
                // layers wrap it as 0x80070102). Anything else - device removed, abandoned
                // mutex - is terminal.
                0x102 or unchecked((int)0x80070102) => AcquireResult.Busy,
                _ => AcquireResult.Failed,
            };
        }

        public bool Release() => _mutex.Handle is not null && _mutex.Get().ReleaseSync(1) == 0;

        // The complement of the writer's protocol: the writer acquired 0 and released 1, so
        // the compositor presents with (1, 0).
        public Task PresentAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image) =>
            surface.UpdateWithKeyedMutexAsync(image, 1, 0);

        // Nothing transfers on import: a KMT global shared handle is not an owned resource,
        // and neither the engine's Vulkan import nor Avalonia's OpenSharedResource1 consumes
        // an NT handle - it stays this side's to close.
        public void HandleImported()
        {
        }

        public void Dispose()
        {
            rd.FreeRid(rid);
            _mutex.Dispose();
            _mutex = default;
            _texture.Dispose();
            _texture = default;
            if (ownsHandle) CloseHandle(sharedHandle);
        }
    }
}
