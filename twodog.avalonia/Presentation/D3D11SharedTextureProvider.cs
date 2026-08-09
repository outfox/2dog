using System;
using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace twodog.Presentation;

/// <summary>
/// Owns a D3D11 keyed-mutex shared texture: the host-side half of the Windows zero-copy
/// path. The engine imports the KMT handle into its Vulkan device; Avalonia imports the
/// same handle into its compositor. The keyed mutex is driven from here (CPU side) around
/// the engine's copy - writer acquires key 0 and releases key 1.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class D3D11SharedTextureProvider : IDisposable
{
    private ComPtr<ID3D11Device> _device;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<ID3D11Texture2D> _texture;
    private ComPtr<IDXGIKeyedMutex> _mutex;

    public nint SharedHandle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Creates the device on the adapter with the given LUID (the compositor's device).</summary>
    public D3D11SharedTextureProvider(byte[]? adapterLuid)
    {
        var api = D3D11.GetApi(null, false);
        using var adapter = FindAdapter(adapterLuid);

        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        // A specific adapter requires DriverType Unknown per D3D11CreateDevice rules.
        SilkMarshal.ThrowHResult(api.CreateDevice(
            (IDXGIAdapter*)adapter.Handle,
            adapter.Handle is null ? D3DDriverType.Hardware : D3DDriverType.Unknown,
            0, (uint)CreateDeviceFlag.BgraSupport, null, 0, D3D11.SdkVersion,
            &device, null, &context));
        _device = device;
        _context = context;
    }

    private static ComPtr<IDXGIAdapter1> FindAdapter(byte[]? luid)
    {
        if (luid is not { Length: 8 }) return default;
        var target = BitConverter.ToInt64(luid, 0);

        var dxgi = DXGI.GetApi(null, false);
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

    /// <summary>(Re)creates the shared texture; any previous one must be released by all importers first.</summary>
    public void CreateTexture(int width, int height)
    {
        ReleaseTexture();

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
        _texture = texture;

        IDXGIKeyedMutex* mutex = null;
        var mutexIid = IDXGIKeyedMutex.Guid;
        SilkMarshal.ThrowHResult(texture->QueryInterface(&mutexIid, (void**)&mutex));
        _mutex = mutex;

        IDXGIResource* resource = null;
        var resourceIid = IDXGIResource.Guid;
        SilkMarshal.ThrowHResult(texture->QueryInterface(&resourceIid, (void**)&resource));
        try
        {
            void* shared = null;
            SilkMarshal.ThrowHResult(resource->GetSharedHandle(&shared));
            SharedHandle = (nint)shared;
        }
        finally
        {
            resource->Release();
        }

        Width = width;
        Height = height;
    }

    /// <summary>Acquire key 0 for writing; false on timeout (compositor still reading).</summary>
    public bool Acquire(uint timeoutMs) =>
        _mutex.Handle is not null && _mutex.Get().AcquireSync(0, timeoutMs) == 0;

    /// <summary>Release with key 1: hands the texture to the compositor.</summary>
    public void Release()
    {
        if (_mutex.Handle is not null)
            SilkMarshal.ThrowHResult(_mutex.Get().ReleaseSync(1));
    }

    private void ReleaseTexture()
    {
        _mutex.Dispose();
        _mutex = default;
        _texture.Dispose();
        _texture = default;
        SharedHandle = 0;
        Width = Height = 0;
    }

    public void Dispose()
    {
        ReleaseTexture();
        _context.Dispose();
        _context = default;
        _device.Dispose();
        _device = default;
    }
}
