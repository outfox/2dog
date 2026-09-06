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
    private static readonly SemaphoreSlim EngineLease = new(1, 1);
    private readonly string _viewId = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _lifetime;
    // Keys the canvas element. Bumped only after an exit: changing it mid-lifetime would make the next render
    // replace the canvas Godot draws into (the first lifetime went black that way, its counter also being the key).
    private int _canvasGeneration;
    private TaskCompletionSource? _canvasRendered;
    private ElementReference _canvas;
    private IJSObjectReference? _module;
    private bool _disposed;
    private bool _starting;
    private bool _hasEngineLease;
    private Task? _disposeTask;

    private string CanvasId => $"twodog-canvas-{_viewId}-{_canvasGeneration}";

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
    public Task StartAsync() => InvokeAsync(StartCoreAsync);

    private async Task StartCoreAsync()
    {
        Engine? startedEngine = null;
        Exception? failure = null;
        await _lifecycle.WaitAsync();
        try
        {
            if (IsRunning || _disposed) return;
            _starting = true;
            Error = null;
            StateHasChanged();

            if (_canvasRendered is { } rendered)
            {
                await rendered.Task.WaitAsync(_disposeCancellation.Token);
                _canvasRendered = null;
            }

            if (PluginsInitializer == IntPtr.Zero)
                throw new ArgumentException($"{nameof(GodotView)}: {nameof(PluginsInitializer)} is required " +
                                            "(pass TwoDogWebBoot.PluginsInitializer() from your game project).");

            await EngineLease.WaitAsync(_disposeCancellation.Token);
            _hasEngineLease = true;
            if (_disposed) return;

            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", _disposeCancellation.Token,
                "./_content/2dog.blazor/2dog.blazor.js");

            // The pack is copied into the wasm file system under its own name; Godot opens it from there.
            var packName = Path.GetFileName(PackUrl);
            await _module.InvokeVoidAsync("prepare", _disposeCancellation.Token, _canvas, new
            {
                packUrl = PackUrl,
                packName,
                resize = (int)Resize,
                focusCanvas = FocusCanvas,
                locale = Locale,
            });
            if (_disposed) return;

            Engine.RegisterWebPluginsInitializer(PluginsInitializer);
            // Blazor keeps running after Godot quits; only the instance goes away.
            Engine.WebExitRuntimeOnQuit = false;

            var args = new List<string> { "--main-pack", packName };
            if (Args is not null) args.AddRange(Args);

            var engine = new Engine(Project, args: args.ToArray());
            Engine = engine;
            try
            {
                engine.Exited += () => _ = InvokeAsync(() => CompleteExitAsync(engine));
                engine.Start();
                _lifetime++;
                // Hands the loop to emscripten and returns; the engine destroys itself on quit.
                engine.Run(() => { if (!_disposed) OnFrame?.Invoke(); });
                startedEngine = engine;
            }
            catch
            {
                await StopEngineAsync(engine);
                throw;
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            await CleanupStartAttemptAsync();
        }
        catch (Exception e)
        {
            // An exception escaping OnAfterRenderAsync takes the whole Blazor app down; report instead.
            Console.Error.WriteLine($"{nameof(GodotView)}: {e}");
            Error = e;
            failure = e;
            try
            {
                await CleanupStartAttemptAsync();
            }
            catch (Exception cleanupError)
            {
                Console.Error.WriteLine($"{nameof(GodotView)}: startup cleanup failed: {cleanupError}");
                failure = new AggregateException(e, cleanupError);
                Error = failure;
            }
        }
        finally
        {
            _starting = false;
            if (_disposed)
            {
                try
                {
                    await CleanupStartAttemptAsync();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"{nameof(GodotView)}: disposal cleanup failed: {e}");
                }
            }
            _lifecycle.Release();
        }

        if (startedEngine is not null && !_disposed && ReferenceEquals(Engine, startedEngine))
        {
            try
            {
                await Started.InvokeAsync(startedEngine);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{nameof(GodotView)}: {nameof(Started)} callback failed: {e}");
                failure = e;
                Error = e;
                await _lifecycle.WaitAsync();
                try
                {
                    if (ReferenceEquals(Engine, startedEngine))
                        await StopEngineAsync(startedEngine);
                }
                catch (Exception cleanupError)
                {
                    Console.Error.WriteLine($"{nameof(GodotView)}: callback cleanup failed: {cleanupError}");
                    failure = new AggregateException(e, cleanupError);
                    Error = failure;
                }
                finally
                {
                    _lifecycle.Release();
                }
            }
        }

        if (failure is not null && !_disposed)
        {
            StateHasChanged();
            await Failed.InvokeAsync(failure);
        }
    }

    private async Task CleanupStartAttemptAsync()
    {
        if (Engine is { } engine)
        {
            await StopEngineAsync(engine);
            return;
        }

        try
        {
            await CleanupJsAsync();
        }
        finally
        {
            ReleaseEngineLease();
        }
    }

    private async Task<bool> StopEngineAsync(Engine? engine)
    {
        var stoppedCurrent = false;
        if (engine is not null)
        {
            try
            {
                await engine.DisposeAsync();
            }
            finally
            {
                if (ReferenceEquals(Engine, engine))
                {
                    stoppedCurrent = true;
                    Engine = null;
                    try
                    {
                        await CleanupJsAsync();
                    }
                    finally
                    {
                        ReleaseEngineLease();
                    }
                }
            }
        }

        if (!stoppedCurrent || _disposed) return false;

        // Godot keeps one WebGL context per canvas element; the next lifetime gets a new element.
        _canvasGeneration++;
        _canvasRendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StateHasChanged();
        return true;
    }

    private async Task CleanupJsAsync()
    {
        if (_module is null) return;
        var module = _module;
        _module = null;
        try
        {
            await module.InvokeVoidAsync("release", _canvas);
        }
        catch (JSDisconnectedException)
        {
            // Page is going away.
        }
        catch (JSException e)
        {
            Console.Error.WriteLine($"{nameof(GodotView)}: JavaScript release failed: {e.Message}");
        }

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Page is going away.
        }
        catch (JSException e)
        {
            Console.Error.WriteLine($"{nameof(GodotView)}: JavaScript module disposal failed: {e.Message}");
        }
    }

    private void ReleaseEngineLease()
    {
        if (!_hasEngineLease) return;
        _hasEngineLease = false;
        EngineLease.Release();
    }

    /// <summary>Asks Godot to quit (like closing its window); teardown completes asynchronously, then <see cref="Exited"/> fires.</summary>
    public void Quit()
    {
        if (!IsRunning) return;
        Engine!.RequestQuit();
    }

    private async Task CompleteExitAsync(Engine engine)
    {
        var notifyExited = false;
        try
        {
            await _lifecycle.WaitAsync();
            try
            {
                if (!ReferenceEquals(Engine, engine)) return;
                notifyExited = await StopEngineAsync(engine);
            }
            finally
            {
                _lifecycle.Release();
            }

            if (notifyExited)
                await Exited.InvokeAsync();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"{nameof(GodotView)}: exit handling failed: {e}");
        }
    }

    /// <summary>Focuses the canvas so keyboard input reaches Godot.</summary>
    public ValueTask FocusAsync() => _canvas.FocusAsync();

    public ValueTask DisposeAsync() => new(_disposeTask ??= InvokeAsync(DisposeCoreAsync));

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _disposeCancellation.Cancel();
        await _lifecycle.WaitAsync();
        try
        {
            await CleanupStartAttemptAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }
}
