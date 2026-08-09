using System;
using System.Runtime.Versioning;
using Avalonia.Platform;
using Godot;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace twodog.Presentation;

/// <summary>
/// Import-style sharing for Windows: this side creates a D3D11 keyed-mutex shared texture on
/// the compositor's adapter, the engine imports its KMT handle into Vulkan, and Avalonia
/// imports the same handle. The keyed mutex is driven from the CPU around the engine's copy -
/// writer acquires key 0 and releases key 1; the compositor presents with (1, 0).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D11SharedTextureFactory : ISharedTextureFactory
{
    private readonly D3D11 _api;
    private ComPtr<ID3D11Device> _device;

    public string AvaloniaHandleType => KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle;

    /// <summary>Creates the device on the adapter with the given LUID (the compositor's device).</summary>
    public D3D11SharedTextureFactory(byte[]? adapterLuid)
    {
        _api = D3D11.GetApi(null, false);
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
            MiscFlags = (uint)ResourceMiscFlag.SharedKeyedmutex,
        };
        ID3D11Texture2D* texture = null;
        SilkMarshal.ThrowHResult(_device.Get().CreateTexture2D(&desc, null, &texture));

        IDXGIKeyedMutex* mutex = null;
        var mutexIid = IDXGIKeyedMutex.Guid;
        SilkMarshal.ThrowHResult(texture->QueryInterface(&mutexIid, (void**)&mutex));

        IDXGIResource* resource = null;
        var resourceIid = IDXGIResource.Guid;
        SilkMarshal.ThrowHResult(texture->QueryInterface(&resourceIid, (void**)&resource));
        nint shared;
        try
        {
            void* handle = null;
            SilkMarshal.ThrowHResult(resource->GetSharedHandle(&handle));
            shared = (nint)handle;
        }
        finally
        {
            resource->Release();
        }

        var rid = rd.ExternalTextureCreate(
            RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex,
            RenderingDevice.DataFormat.R8G8B8A8Unorm,
            (uint)width, (uint)height, (ulong)shared);
        if (!rid.IsValid)
        {
            mutex->Release();
            texture->Release();
            throw new NotSupportedException("The engine could not import the D3D11 shared texture.");
        }

        return new D3D11SharedTexture(rd, rid, texture, mutex, shared, width, height);
    }

    public void Dispose()
    {
        _device.Dispose();
        _device = default;
        // Last: the API wrapper's native-library context must outlive objects created through it.
        _api.Dispose();
    }

    private sealed class D3D11SharedTexture(
        RenderingDevice rd, Rid rid, ID3D11Texture2D* texture, IDXGIKeyedMutex* mutex,
        nint sharedHandle, int width, int height) : ISharedTexture
    {
        private ComPtr<ID3D11Texture2D> _texture = texture;
        private ComPtr<IDXGIKeyedMutex> _mutex = mutex;

        public int Width => width;
        public int Height => height;
        public Rid Rid => rid;

        public IPlatformHandle Handle { get; } = new PlatformHandle(sharedHandle,
            KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle);

        public PlatformGraphicsExternalImageProperties ImportProperties => new()
        {
            Width = width,
            Height = height,
            Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
            // The engine writes rows top-down; without this the GL-backed compositor assumes
            // a bottom-left origin and shows the viewport flipped.
            TopLeftOrigin = true,
        };

        public SharedTextureSync Sync => SharedTextureSync.KeyedMutex;

        public AcquireResult Acquire()
        {
            if (_mutex.Handle is null) return AcquireResult.Failed;
            return _mutex.Get().AcquireSync(0, 100) switch
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

        public void Dispose()
        {
            rd.FreeRid(rid);
            _mutex.Dispose();
            _mutex = default;
            _texture.Dispose();
            _texture = default;
        }
    }
}
