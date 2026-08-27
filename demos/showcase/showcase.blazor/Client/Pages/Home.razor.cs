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
    private float _spinSpeed = 1.5f;
    private bool _paused;
    private readonly List<SpinningCube> _cubes = [];

    private bool IsRunning => _view?.IsRunning == true;

    private void OnStarted(Engine engine)
    {
        var tree = engine.Tree;
        _status = "Running";
        _engineVersion = $"Godot {Godot.Engine.GetVersionInfo()["string"]} / 2dog {Engine.Version}";
        _sceneName = tree.CurrentScene?.Name ?? "-";

        foreach (var node in tree.CurrentScene?.FindChildren("*", nameof(SpinningCube), recursive: true, owned: false) ?? [])
            if (node is SpinningCube cube)
                _cubes.Add(cube);
        if (_cubes.Count > 0)
            _spinSpeed = _cubes[0].SpinSpeed;

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
        // The panel re-renders at a few Hz; the engine keeps rendering every frame regardless.
        if (_frames % 30 != 0) return;
        _fps = Godot.Engine.GetFramesPerSecond().ToString("0");
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnExited()
    {
        _status = "Godot quit. Reload the page to start it again.";
        _cubes.Clear();
    }

    private void OnFailed(Exception exception) => _status = $"Failed: {exception.Message}";

    private void OnSpinSpeed(ChangeEventArgs e)
    {
        if (!float.TryParse(e.Value?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var speed))
            return;
        _spinSpeed = speed;
        foreach (var cube in _cubes)
            cube.SpinSpeed = speed;
    }

    private void TogglePause()
    {
        if (_view?.Tree is not { } tree) return;
        _paused = !_paused;
        tree.Paused = _paused;
    }
}
