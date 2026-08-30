using Godot;
using Microsoft.AspNetCore.Components;
using twodog;
using Engine = twodog.Engine;

namespace showcase.blazor.Client.Pages;

// Blazor UI and Godot share one runtime: the panel reads engine state and pokes scene nodes directly.
public partial class Home : ComponentBase
{
    private GodotView? _view;
    private string _status = "Starting Godot...";
    private string _engineVersion = "-";
    private string _sceneName = "-";
    private string _fps = "-";
    private long _frames;
    private float _spinSpeed = 1.8f; // radians per second
    private bool _paused;
    private Node3D[] _whiteCubes = [];
    private static readonly Vector3 WhiteSpinAxis = new Vector3(1, 1, 0).Normalized();

    private bool IsRunning => _view?.IsRunning == true;

    private void OnStarted(Engine engine)
    {
        var tree = engine.Tree;
        _status = "Running";
        _engineVersion = $"Godot {Godot.Engine.GetVersionInfo()["string"]} / 2dog {Engine.Version}";
        _sceneName = tree.CurrentScene?.Name ?? "-";
        // CI's restart probe reads this to tell the second lifetime's smoke pass from the first.
        JavaScriptBridge.Eval($"document.documentElement.setAttribute('data-twodog-lifetime', '{_view?.Lifetime ?? 0}')");

        // The blue cubes spin themselves via SpinningCube._Process (Godot side), the red ones via GDScript;
        // the white ones are plain MeshInstance3Ds this host drives from its frame callback.
        _whiteCubes = tree.CurrentScene?.GetNodeOrNull<Node3D>("Flair/WhiteCubes")?
            .GetChildren().OfType<Node3D>().ToArray() ?? [];

        RunSmoke(tree);
    }

    // The same API probe the web host runs; CI's headless browser waits for the marker it sets.
    private static void RunSmoke(SceneTree tree)
    {
        try
        {
            GodotApiSmoke.RunAll(tree);
            JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'passed')");
            Console.WriteLine("2DOG_WASM_SMOKE_PASSED");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"2DOG_WASM_SMOKE_FAILED: {exception}");
            JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'failed')");
        }
    }

    private void OnFrame()
    {
        _frames++;
        if (!_paused)
        {
            var delta = (float)(_view?.Tree?.Root.GetProcessDeltaTime() ?? 0);
            foreach (var cube in _whiteCubes)
                cube.Rotate(WhiteSpinAxis, _spinSpeed * delta);
        }
        // The panel re-renders at a few Hz; the engine keeps rendering every frame regardless.
        if (_frames % 30 != 0) return;
        _fps = Godot.Engine.GetFramesPerSecond().ToString("0");
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnExited()
    {
        _status = "Godot quit.";
        _whiteCubes = [];
        _paused = false;
    }

    private void OnFailed(Exception exception) => _status = $"Failed: {exception.Message}";

    private void OnSpinSpeed(ChangeEventArgs e)
    {
        if (!float.TryParse(e.Value?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var speed))
            return;
        _spinSpeed = speed;
    }

    private void TogglePause()
    {
        if (_view?.Tree is not { } tree) return;
        _paused = !_paused;
        tree.Paused = _paused;
    }
}
