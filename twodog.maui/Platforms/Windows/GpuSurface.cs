using System.Runtime.InteropServices;
using Godot;
using Microsoft.UI.Xaml.Controls;
using WinRT;
using static twodog.Presentation.D3D11Interop;

namespace twodog.Maui;

// The WinUI composition swap chain and Godot's Vulkan device share a D3D11 texture.
// ExternalTexturePresent flushes/stalls Godot's writes; the keyed mutex orders the
// D3D11 copy before Vulkan reuses the texture. No image data reaches the CPU.
internal sealed unsafe class GpuSurface : IDisposable
{
    [ComImport, Guid("63aad0b8-7c24-40ff-85a8-640d944cc325"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISwapChainPanelNative
    {
        void SetSwapChain(nint swapChain);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SwapChainDesc
    {
        public uint Width, Height, Format;
        public int Stereo;
        public uint SampleCount, SampleQuality, Usage, BufferCount, Scaling, SwapEffect, AlphaMode, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix3x2
    {
        public float M11, M12, M21, M22, M31, M32;
    }

    private readonly SwapChainPanel _panel;
    private readonly RenderingDevice _rd;
    private void* _device;
    private void* _context;
    private void* _swapChain;
    private void* _texture;
    private void* _mutex;
    private nint _sharedHandle;
    private bool _ownsHandle;
    private Rid _imported;
    private int _width, _height;

    public GpuSurface(SwapChainPanel panel, RenderingDevice rd)
    {
        _panel = panel;
        _rd = rd;
        try
        {
            void* device = null;
            void* context = null;
            ThrowIfFailed(D3D11CreateDevice(null, DriverTypeHardware, 0, CreateDeviceBgraSupport,
                null, 0, SdkVersion, &device, null, &context));
            _device = device;
            _context = context;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static void** Vtable(void* value) => *(void***)value;

    public void Present(Rid viewport, int width, int height)
    {
        if (width != _width || height != _height) Resize(width, height);
        var acquire = AcquireSync(_mutex, 0, 0);
        if (acquire == 0x102 || acquire == unchecked((int)0x80070102)) return;
        // WAIT_ABANDONED has success severity but the resource is no longer usable.
        if (acquire != 0) throw new InvalidOperationException($"GPU keyed mutex failed: 0x{acquire:x8}.");
        try
        {
            var source = RenderingServer.TextureGetRdTexture(viewport);
            if (_rd.ExternalTexturePresent(source, _imported) != Error.Ok)
                throw new InvalidOperationException("Godot could not present its viewport to the shared GPU texture.");
            var textureIid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            void* backBuffer = null;
            ThrowIfFailed(((delegate* unmanaged[Stdcall]<void*, uint, Guid*, void**, int>)Vtable(_swapChain)[9])(
                _swapChain, 0, &textureIid, &backBuffer));
            try
            {
                ((delegate* unmanaged[Stdcall]<void*, void*, void*, void>)Vtable(_context)[47])(
                    _context, backBuffer, _texture);
                ((delegate* unmanaged[Stdcall]<void*, void>)Vtable(_context)[111])(_context); // Flush
            }
            finally { Release(backBuffer); }
        }
        finally { ThrowIfFailed(ReleaseSync(_mutex, 0)); }
        // Composition owns presentation pacing. DO_NOT_WAIT prevents the UI thread waiting
        // on a saturated presentation queue; DXGI_ERROR_WAS_STILL_DRAWING skips that frame.
        var result = ((delegate* unmanaged[Stdcall]<void*, uint, uint, int>)Vtable(_swapChain)[8])(
            _swapChain, 0, 8);
        if (result != unchecked((int)0x887a000a)) ThrowIfFailed(result);
    }

    private void Resize(int width, int height)
    {
        ReleaseBuffers();
        try
        {
            var desc = new SwapChainDesc
            {
                Width = (uint)width, Height = (uint)height, Format = FormatR8G8B8A8Unorm,
                SampleCount = 1, Usage = 0x20, BufferCount = 2, SwapEffect = 4, AlphaMode = 3,
            };
            var factory = TryCreateFactory1();
            if (factory is null) throw new NotSupportedException("DXGI is unavailable.");
            void* factory2 = null;
            try
            {
                var iid = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
                ThrowIfFailed(QueryInterface(factory, &iid, &factory2));
                void* swapChain = null;
                ThrowIfFailed(((delegate* unmanaged[Stdcall]<void*, void*, SwapChainDesc*, void*, void**, int>)Vtable(factory2)[24])(
                    factory2, _device, &desc, null, &swapChain));
                _swapChain = swapChain;
            }
            finally
            {
                if (factory2 is not null) Release(factory2);
                Release(factory);
            }
            SetScale();
            CreateSharedTexture(width, height);
            _panel.As<ISwapChainPanelNative>().SetSwapChain((nint)_swapChain);
            _width = width;
            _height = height;
        }
        catch
        {
            ReleaseBuffers();
            throw;
        }
    }

    public void SetScale()
    {
        if (_swapChain is null) return;
        void* swapChain2 = null;
        var iid = new Guid("a8be2ac4-199f-4946-b331-79599fb98de7");
        ThrowIfFailed(QueryInterface(_swapChain, &iid, &swapChain2));
        try
        {
            var matrix = new Matrix3x2 { M11 = 1 / _panel.CompositionScaleX, M22 = 1 / _panel.CompositionScaleY };
            ThrowIfFailed(((delegate* unmanaged[Stdcall]<void*, Matrix3x2*, int>)Vtable(swapChain2)[34])(
                swapChain2, &matrix));
        }
        finally { Release(swapChain2); }
    }

    private void CreateSharedTexture(int width, int height)
    {
        var mask = _rd.ExternalTextureGetSupportedHandleTypes();
        var ntType = RenderingDevice.ExternalTextureShareHandleType.D3D11NtKeyedMutex;
        var kmtType = RenderingDevice.ExternalTextureShareHandleType.D3D11KmtKeyedMutex;
        _ownsHandle = (mask & (1u << (int)ntType)) != 0;
        if (!_ownsHandle && (mask & (1u << (int)kmtType)) == 0)
            throw new NotSupportedException("This GPU/renderer cannot share D3D11 textures. Select the Vulkan renderer.");
        var desc = new Texture2DDesc
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = FormatR8G8B8A8Unorm, SampleCount = 1, BindFlags = BindRenderTarget | BindShaderResource,
            MiscFlags = MiscSharedKeyedMutex | (_ownsHandle ? MiscSharedNtHandle : 0),
        };
        void* texture = null;
        ThrowIfFailed(CreateTexture2D(_device, &desc, null, &texture));
        _texture = texture;
        var mutexIid = IidKeyedMutex;
        void* mutex = null;
        ThrowIfFailed(QueryInterface(_texture, &mutexIid, &mutex));
        _mutex = mutex;
        void* resource = null;
        var resourceIid = _ownsHandle ? IidResource1 : IidResource;
        ThrowIfFailed(QueryInterface(_texture, &resourceIid, &resource));
        try
        {
            nint handle = 0;
            ThrowIfFailed(_ownsHandle
                ? CreateSharedHandle(resource, null, 0x80000001, null, &handle)
                : GetSharedHandle(resource, &handle));
            _sharedHandle = handle;
        }
        finally { Release(resource); }
        _imported = _rd.ExternalTextureCreate(_ownsHandle ? ntType : kmtType,
            RenderingDevice.DataFormat.R8G8B8A8Unorm, (uint)width, (uint)height, (ulong)_sharedHandle);
        if (!_imported.IsValid) throw new NotSupportedException("Godot could not import the shared D3D11 texture.");
    }

    private void ReleaseBuffers()
    {
        if (_swapChain is not null) _panel.As<ISwapChainPanelNative>().SetSwapChain(0);
        if (_imported.IsValid) _rd.FreeRid(_imported);
        _imported = default;
        if (_mutex is not null) Release(_mutex);
        _mutex = null;
        if (_texture is not null) Release(_texture);
        _texture = null;
        if (_ownsHandle && _sharedHandle != 0) CloseHandle(_sharedHandle);
        _sharedHandle = 0;
        if (_swapChain is not null) Release(_swapChain);
        _swapChain = null;
        _width = _height = 0;
    }

    public void Dispose()
    {
        ReleaseBuffers();
        if (_context is not null) Release(_context);
        _context = null;
        if (_device is not null) Release(_device);
        _device = null;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
