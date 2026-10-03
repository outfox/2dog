using System.Runtime.Versioning;

namespace twodog.Presentation;

/// <summary>The default DXGI adapter - the one Avalonia's D3D11-backed compositor renders on.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe class DefaultAdapter
{
    /// <summary>LUID as (HighPart &lt;&lt; 32) | LowPart; null when enumeration fails or the
    /// default adapter is software (WARP cannot share with a hardware Vulkan device).</summary>
    public static long? TryGetLuid()
    {
        var factory = D3D11Interop.TryCreateFactory1();
        if (factory is null)
            return null;
        try
        {
            void* adapter = null;
            if (D3D11Interop.EnumAdapters1(factory, 0, &adapter) != 0)
                return null;
            try
            {
                D3D11Interop.AdapterDesc1 desc;
                D3D11Interop.GetDesc1(adapter, &desc);
                if ((desc.Flags & D3D11Interop.AdapterFlagSoftware) != 0)
                    return null;
                return desc.Luid;
            }
            finally
            {
                D3D11Interop.Release(adapter);
            }
        }
        finally
        {
            D3D11Interop.Release(factory);
        }
    }
}
