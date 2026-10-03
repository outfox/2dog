using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace twodog.Presentation;

/// <summary>
/// The few DXGI/D3D11 calls the Windows zero-copy path makes, as raw COM vtable calls. Binding libraries probe for
/// their native libraries through assembly file locations, which trimmed and NativeAOT publishes do not have.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class D3D11Interop
{
    public const uint FormatR8G8B8A8Unorm = 28;
    public const uint UsageDefault = 0;
    public const uint BindShaderResource = 0x8;
    public const uint BindRenderTarget = 0x20;
    public const uint MiscSharedKeyedMutex = 0x100;
    public const uint MiscSharedNtHandle = 0x800;
    public const uint CreateDeviceBgraSupport = 0x20;
    public const uint DriverTypeUnknown = 0;
    public const uint DriverTypeHardware = 1;
    public const uint SdkVersion = 7;
    public const uint AdapterFlagSoftware = 2;

    public static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    public static readonly Guid IidKeyedMutex = new("9d8e1289-d7b3-465f-8126-250e349af85d");
    public static readonly Guid IidResource = new("035f3ab4-482e-4e50-b41f-8a7f8bd8960b");
    public static readonly Guid IidResource1 = new("30961379-4609-4a41-998e-54fe567ee0c1");

    /// <summary>DXGI_ADAPTER_DESC1.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;

        /// <summary>LUID as (HighPart &lt;&lt; 32) | LowPart.</summary>
        public readonly long Luid => ((long)LuidHighPart << 32) | LuidLowPart;
    }

    /// <summary>D3D11_TEXTURE2D_DESC.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* riid, void** factory);

    [LibraryImport("d3d11.dll")]
    public static partial int D3D11CreateDevice(void* adapter, uint driverType, nint software, uint flags,
        uint* featureLevels, uint featureLevelCount, uint sdkVersion, void** device, uint* featureLevel,
        void** immediateContext);

    /// <summary>A new IDXGIFactory1, or null when DXGI refuses.</summary>
    public static void* TryCreateFactory1()
    {
        var iid = IidFactory1;
        void* factory = null;
        return CreateDXGIFactory1(&iid, &factory) < 0 ? null : factory;
    }

    // Slots count IUnknown (0-2) and every base interface; see dxgi.h and d3d11.h.
    private static void** Vtbl(void* com) => *(void***)com;

    public static int QueryInterface(void* com, Guid* iid, void** result) =>
        ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)Vtbl(com)[0])(com, iid, result);

    public static uint Release(void* com) =>
        ((delegate* unmanaged[Stdcall]<void*, uint>)Vtbl(com)[2])(com);

    // IDXGIFactory1
    public static int EnumAdapters1(void* factory, uint index, void** adapter) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)Vtbl(factory)[12])(factory, index, adapter);

    // IDXGIAdapter1
    public static int GetDesc1(void* adapter, AdapterDesc1* desc) =>
        ((delegate* unmanaged[Stdcall]<void*, AdapterDesc1*, int>)Vtbl(adapter)[10])(adapter, desc);

    // ID3D11Device
    public static int CreateTexture2D(void* device, Texture2DDesc* desc, void* initialData, void** texture) =>
        ((delegate* unmanaged[Stdcall]<void*, Texture2DDesc*, void*, void**, int>)Vtbl(device)[5])(
            device, desc, initialData, texture);

    // IDXGIResource
    public static int GetSharedHandle(void* resource, nint* handle) =>
        ((delegate* unmanaged[Stdcall]<void*, nint*, int>)Vtbl(resource)[8])(resource, handle);

    // IDXGIResource1
    public static int CreateSharedHandle(void* resource, void* attributes, uint access, char* name, nint* handle) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, uint, char*, nint*, int>)Vtbl(resource)[13])(
            resource, attributes, access, name, handle);

    // IDXGIKeyedMutex
    public static int AcquireSync(void* mutex, ulong key, uint milliseconds) =>
        ((delegate* unmanaged[Stdcall]<void*, ulong, uint, int>)Vtbl(mutex)[8])(mutex, key, milliseconds);

    public static int ReleaseSync(void* mutex, ulong key) =>
        ((delegate* unmanaged[Stdcall]<void*, ulong, int>)Vtbl(mutex)[9])(mutex, key);

    public static void ThrowIfFailed(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }
}
