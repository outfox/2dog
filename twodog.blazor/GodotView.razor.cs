using Godot;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace twodog;

/// <summary>How the GodotView sizes its canvas. Values mirror Godot's web canvasResizePolicy.</summary>
public enum GodotCanvasResize
{
    /// <summary>The canvas fills its container element and follows its size (default).</summary>
    Container = 0,
    /// <summary>The canvas takes the project's window size.</summary>
    Project = 1,
    /// <summary>The canvas covers the whole browser window.</summary>
    FullWindow = 2,
}

/// <summary>
/// Hosts the Godot engine on a canvas inside a Blazor WebAssembly page. Godot is statically linked into the
/// runtime Blazor booted, so the engine runs on the page's thread and Razor code reaches Godot objects directly
/// (via <see cref="Engine"/> / <see cref="Tree"/>). One engine at a time: after <see cref="Quit"/> completes
/// (<see cref="Exited"/>), <see cref="StartAsync"/> starts a new one on a fresh canvas element.
/// </summary>
public partial class GodotView : ComponentBase, IAsyncDisposable
{
    private readonly string _viewId = Guid.NewGuid().ToString("N");
    private int _lifetime;
    private TaskCompletionSource? _canvasRendered;
    private ElementReference _canvas;
    private IJSObjectReference? _module;
    private bool _disposed;
    private bool _starting;

    private string CanvasId => $"twodog-canvas-{_viewId}-{_lifetime}";

    [Inject] private IJSRuntime Js { get; set; } = default!;

    /// <summary>Label Godot sees as its first argument; conventionally the game project name.</summary>
    [Parameter, EditorRequired] public string Project { get; set; } = "";

    /// <summary>
    /// The game assembly's plugins-initializer pointer, <c>TwoDogWebBoot.PluginsInitializer()</c> from the 2dog
    /// template (there is no GodotPlugins.dll on web).
    /// </summary>
    [Parameter, EditorRequired] public IntPtr PluginsInitializer { get; set; }

    /// <summary>URL of the exported game pack, relative to the document base (the 2dog build publishes it as godot.pck).</summary>
    [Parameter] public string PackUrl { get; set; } = "godot.pck";

    /// <summary>Additional Godot command-line arguments (after the pack argument).</summary>
    [Parameter] public IReadOnlyList<string>? Args { get; set; }

    [Parameter] public GodotCanvasResize Resize { get; set; } = GodotCanvasResize.Container;

    /// <summary>Focus the canvas once the engine starts so it receives keyboard input.</summary>
    [Parameter] public bool FocusCanvas { get; set; } = true;

    /// <summary>Locale reported to Godot; null = the browser's language.</summary>
    [Parameter] public string? Locale { get; set; }

    /// <summary>Start the engine after the first render (default); false leaves it to <see cref="StartAsync"/>.</summary>
    [Parameter] public bool AutoStart { get; set; } = true;

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }

    /// <summary>Content rendered on top of the canvas (positioned within the view's container).</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Invoked once per engine frame, after the iteration. Keep it cheap: it runs on the render thread.</summary>
    [Parameter] public Action? OnFrame { get; set; }

    /// <summary>The engine started and its main loop runs; the scene tree is available.</summary>
    [Parameter] public EventCallback<Engine> Started { get; set; }

    /// <summary>Godot quit and the instance was destroyed.</summary>
    [Parameter] public EventCallback Exited { get; set; }

    /// <summary>Starting the engine failed; <see cref="Error"/> holds the exception and the view shows it.</summary>
    [Parameter] public EventCallback<Exception> Failed { get; set; }

    /// <summary>The running engine, null before start and after exit.</summary>
    public Engine? Engine { get; private set; }

    /// <summary>The running engine's scene tree, null unless <see cref="IsRunning"/>.</summary>
    public SceneTree? Tree => IsRunning ? Engine!.Tree : null;

    public bool IsRunning => Engine is not null;

    /// <summary>Number of engines this view has started (informational).</summary>
    public int Lifetime => _lifetime;

    /// <summary><see cref="StartAsync"/> would start an engine now.</summary>
    public bool CanStart => !IsRunning && !_starting && !_disposed;

    public Exception? Error { get; private set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The fresh canvas of a restart is in the DOM once this render completed.
        _canvasRendered?.TrySetResult();
        // Server-side prerendering never gets here (no OnAfterRender there), but guard the platform anyway.
        if (!firstRender || !OperatingSystem.IsBrowser() || _disposed || !AutoStart) return;
        await StartAsync();
    }

    /// <summary>Starts the engine; a no-op while one is running or starting. Works again after <see cref="Exited"/>.</summary>
    public async Task StartAsync()
    {
        if (IsRunning || _starting || _disposed) return;
        _starting = true;
        Error = null;
        StateHasChanged();
        try
        {
            if (_canvasRendered is { } rendered)
            {
                await rendered.Task;
                _canvasRendered = null;
            }

            if (PluginsInitializer == IntPtr.Zero)
                throw new ArgumentException($"{nameof(GodotView)}: {nameof(PluginsInitializer)} is required " +
                                            "(pass TwoDogWebBoot.PluginsInitializer() from your game project).");

            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", "./_content/2dog.blazor/2dog.blazor.js");

            // The pack is copied into the wasm file system under its own name; Godot opens it from there.
            var packName = Path.GetFileName(PackUrl);
            await _module.InvokeVoidAsync("prepare", _canvas, new
            {
                packUrl = PackUrl,
                packName,
                resize = (int)Resize,
                focusCanvas = FocusCanvas,
                locale = Locale,
            });
            if (_disposed) return;

            if (_lifetime == 0) _lifetime = 1;
            Engine.RegisterWebPluginsInitializer(PluginsInitializer);
            // Blazor keeps running after Godot quits; only the instance goes away.
            Engine.WebExitRuntimeOnQuit = false;

            var args = new List<string> { "--main-pack", packName };
            if (Args is not null) args.AddRange(Args);

            var engine = new Engine(Project, args: args.ToArray());
            engine.Exited += OnEngineExited;
            engine.Start();
            Engine = engine;
            // Hands the loop to emscripten and returns; the engine destroys itself on quit.
            engine.Run(() => OnFrame?.Invoke());

            await Started.InvokeAsync(engine);
        }
        catch (Exception e)
        {
            // An exception escaping OnAfterRenderAsync takes the whole Blazor app down; report instead.
            Console.Error.WriteLine($"{nameof(GodotView)}: {e}");
            Error = e;
            Engine = null;
            StateHasChanged();
            await Failed.InvokeAsync(e);
        }
        finally
        {
            _starting = false;
        }
    }

    private void OnEngineExited()
    {
        Engine = null;
        // Godot keeps one WebGL context per canvas element; the next lifetime gets a new element.
        _lifetime++;
        _canvasRendered = new TaskCompletionSource();
        _ = InvokeAsync(async () =>
        {
            StateHasChanged();
            await Exited.InvokeAsync();
        });
    }

    /// <summary>Asks Godot to quit (like closing its window); teardown completes asynchronously, then <see cref="Exited"/> fires.</summary>
    public void Quit()
    {
        if (!IsRunning) return;
        Engine!.Tree.Quit();
    }

    /// <summary>Focuses the canvas so keyboard input reaches Godot.</summary>
    public ValueTask FocusAsync() => _canvas.FocusAsync();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Quit();
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("release", _canvas);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Page is going away.
            }
        }
    }
}
