using System.Runtime.Versioning;
using Silk.NET.Core.Native;
using Silk.NET.DXGI;

namespace twodog.Presentation;

/// <summary>The default DXGI adapter - the one Avalonia's D3D11-backed compositor renders on.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe class DefaultAdapter
{
    /// <summary>LUID as (HighPart &lt;&lt; 32) | LowPart; null when enumeration fails or the
    /// default adapter is software (WARP cannot share with a hardware Vulkan device).</summary>
    public static long? TryGetLuid()
    {
        using var dxgi = DXGI.GetApi(null, false);
        IDXGIFactory1* factory = null;
        if (dxgi.CreateDXGIFactory1(SilkMarshal.GuidPtrOf<IDXGIFactory1>(), (void**)&factory) < 0)
            return null;
        try
        {
            IDXGIAdapter1* adapter = null;
            if (factory->EnumAdapters1(0, &adapter) != 0)
                return null;
            try
            {
                AdapterDesc1 desc;
                adapter->GetDesc1(&desc);
                if ((desc.Flags & (uint)AdapterFlag.Software) != 0)
                    return null;
                return ((long)desc.AdapterLuid.High << 32) | (uint)desc.AdapterLuid.Low;
            }
            finally
            {
                adapter->Release();
            }
        }
        finally
        {
            factory->Release();
        }
    }
}
