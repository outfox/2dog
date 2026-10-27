using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Reflection;
using Godot;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using twodog.Presentation;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Stretch = Microsoft.UI.Xaml.Media.Stretch;
using Visibility = Microsoft.UI.Xaml.Visibility;
using WinRT;

namespace twodog.Maui;

internal sealed partial class GodotViewHandler : ViewHandler<GodotView, Grid>
{
    [ComImport, Guid("905a0fef-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBufferByteAccess
    {
        void Buffer(out nint buffer);
    }
    private readonly ConcurrentQueue<Action<SceneTree>> _commands = new();
    private Engine? _engine;
    private GpuSurface? _surface;
    private Rid _viewport;
    private bool _pumping, _stopRequested, _started;
    private bool _framePending;
    private int _generation;
    private readonly SwapChainPanel _panel = new();
    private readonly Microsoft.UI.Xaml.Controls.Image _image = new() { Stretch = Stretch.Fill };
    private WriteableBitmap? _bitmap;

    public GodotViewHandler() : base(ViewMapper) { }
    protected override Grid CreatePlatformView()
    {
        var grid = new Grid { IsTabStop = true, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        grid.Children.Add(_panel);
        grid.Children.Add(_image);
        return grid;
    }

    protected override void ConnectHandler(Grid platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Loaded += Loaded;
        platformView.Unloaded += Unloaded;
        _panel.CompositionScaleChanged += ScaleChanged;
        ConnectInput(platformView);
    }

    protected override void DisconnectHandler(Grid platformView)
    {
        platformView.Loaded -= Loaded;
        platformView.Unloaded -= Unloaded;
        _panel.CompositionScaleChanged -= ScaleChanged;
        DisconnectInput(platformView);
        Stop();
        base.DisconnectHandler(platformView);
    }

    public void Dispatch(Action<SceneTree> action)
    {
        if (_engine is null || !_started || _stopRequested) throw new InvalidOperationException("Godot has not started.");
        _commands.Enqueue(action);
    }

    private void Loaded(object sender, RoutedEventArgs args)
    {
        var generation = _generation;
        PlatformView.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation == _generation && PlatformView.IsLoaded) Start();
        });
    }

    private void Start()
    {
        if (_engine is not null) return;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(VirtualView.Project);
            // GodotPlugins reuses a game already loaded by its .NET host. Without this,
            // a lazy MAUI page can bind scripts in Godot's plugin context before the
            // host first references the game, splitting otherwise identical C# types.
            Assembly.Load(VirtualView.Project);
            _stopRequested = false;
            _panel.Visibility = Visibility.Visible;
            var affinity = DefaultAdapter.TryGetLuid() is { } luid ? new[] { "--gpu-luid", luid.ToString("x") } : [];
            var gpuPreferred = VirtualView.PresentationMode != GodotPresentationMode.Cpu;
            try { StartEngine(gpuPreferred, affinity); }
            catch when (VirtualView.PresentationMode == GodotPresentationMode.Auto)
            {
                _engine?.Dispose();
                _engine = null;
                StartEngine(false, []);
            }
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            if (gpuPreferred)
            {
                try
                {
                    var rd = RenderingServer.GetRenderingDevice()
                        ?? throw new NotSupportedException("The MAUI GPU view requires Vulkan and shared GPU textures.");
                    _surface = new GpuSurface(_panel, rd);
                    VirtualView.ActivePresentationMode = GodotPresentationMode.Gpu;
                    _image.Visibility = Visibility.Collapsed;
                }
                catch when (VirtualView.PresentationMode == GodotPresentationMode.Auto) { UseCpu(); }
            }
            else UseCpu();
            _viewport = RenderingServer.ViewportGetTexture(_engine!.Tree.Root.GetViewportRid());
            CompositionTarget.Rendering += ScheduleFrame;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void ScheduleFrame(object? sender, object args)
    {
        if (_framePending || _engine is null || _stopRequested) return;
        _framePending = true;
        var generation = _generation;
        // Godot pumps Win32 messages inside Update. Enter it between XAML
        // callbacks, never recursively from XAML layout or its rendering callback.
        if (!PlatformView.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (generation != _generation) return;
            _framePending = false;
            Frame();
        })) _framePending = false;
    }

    private void Frame()
    {
        if (_pumping || _engine is null) return;
        _pumping = true;
        try
        {
            while (_commands.TryDequeue(out var command)) command(_engine.Tree);
            if (!_engine.Update())
            {
                _stopRequested = true;
                VirtualView.NotifyQuit();
                return;
            }
            if (_stopRequested) return;
            if (!_started)
            {
                // Start initializes the engine; its first iteration loads the main scene.
                // Match Android's main-loop-started notification before accepting commands.
                _started = true;
                VirtualView.NotifyStarted();
            }
            var scale = PlatformView.XamlRoot.RasterizationScale;
            var width = (int)Math.Round(PlatformView.ActualWidth * scale);
            var height = (int)Math.Round(PlatformView.ActualHeight * scale);
            if (width <= 0 || height <= 0) return;
            if (DisplayServer.WindowGetSize() != new Vector2I(width, height))
                DisplayServer.WindowSetSize(new Vector2I(width, height));
            if (_surface is not null)
            {
                try { _surface.Present(_viewport, width, height); }
                catch when (VirtualView.PresentationMode == GodotPresentationMode.Auto) { UseCpu(); }
            }
            if (_surface is null) PresentCpu();
        }
        catch (Exception exception) { Fail(exception); }
        finally
        {
            _pumping = false;
            if (_stopRequested) Stop();
        }
    }

    private void ScaleChanged(SwapChainPanel sender, object args) => _surface?.SetScale();
    private void StartEngine(bool gpu, string[] affinity)
    {
        _engine = new Engine(VirtualView.Project, VirtualView.ProjectPath,
            ["--hidden-window", "--rendering-method", gpu ? "mobile" : "gl_compatibility",
                "--rendering-driver", gpu ? "vulkan" : "opengl3", .. affinity, .. VirtualView.Arguments]);
        _engine.Start();
    }

    private void UseCpu()
    {
        _surface?.Dispose();
        _surface = null;
        _panel.Visibility = Visibility.Collapsed;
        _image.Visibility = Visibility.Visible;
        VirtualView.ActivePresentationMode = GodotPresentationMode.Cpu;
    }

    private void PresentCpu()
    {
        using var image = RenderingServer.Texture2DGet(_viewport);
        if (image is null || image.IsEmpty()) return;
        var format = image.GetFormat();
        if (format != global::Godot.Image.Format.Rgba8)
        {
            image.Convert(global::Godot.Image.Format.Rgba8);
            if (format is global::Godot.Image.Format.Rgbah or global::Godot.Image.Format.Rgbaf) image.LinearToSrgb();
        }
        var width = image.GetWidth();
        var height = image.GetHeight();
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height);
            _image.Source = _bitmap;
        }
        var data = image.GetData();
        // Godot's readback is already premultiplied. WinUI needs BGRA channel order.
        for (var i = 0; i < data.Length; i += 4)
        {
            var red = data[i];
            data[i] = data[i + 2];
            data[i + 2] = red;
        }
        _bitmap.PixelBuffer.As<IBufferByteAccess>().Buffer(out var pixels);
        Marshal.Copy(data, 0, pixels, data.Length);
        _bitmap.Invalidate();
    }
    private void Unloaded(object sender, RoutedEventArgs args) => Stop();
    private void Fail(Exception exception)
    {
        Stop();
        VirtualView.NotifyFailure(exception);
    }

    private void Stop()
    {
        _stopRequested = true;
        if (_pumping) return; // Update can dispatch an unload/close; dispose between frames.
        CompositionTarget.Rendering -= ScheduleFrame;
        _generation++;
        _framePending = false;
        _surface?.Dispose();
        _surface = null;
        _engine?.Dispose();
        _engine = null;
        _started = false;
        _viewport = default;
        _bitmap = null;
        _image.Source = null;
        if (VirtualView is { } view) view.ActivePresentationMode = null;
        _commands.Clear();
        _buttons = 0;
        _pressedKeys.Clear();
        _touches.Clear();
    }
}
