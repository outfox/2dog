"""Exercise a packed MAUI view from a scaffold outside the repository.

uv run python tests/maui/test_windows.py [--feed packages] [--require-gpu]
Requires the Windows and Android MAUI workloads and current packed packages.
"""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument("--feed", type=Path, default=REPO / "packages")
parser.add_argument("--require-gpu", action="store_true")
options, remaining = parser.parse_known_args()

PAGE = r'''
using twodog.Maui;
namespace MauiSmoke.Maui;
public sealed partial class MainPage : ContentPage
{
    public MainPage()
    {
        var policy = Enum.Parse<GodotPresentationMode>(Environment.GetEnvironmentVariable("TWODOG_MAUI_SMOKE_MODE")!);
        var log = Environment.GetEnvironmentVariable("TWODOG_MAUI_SMOKE_LOG")!;
        var compatibility = Environment.GetEnvironmentVariable("TWODOG_MAUI_SMOKE_COMPATIBILITY") == "1";
        InitializeComponent();
        var game = Game;
        game.PresentationMode = policy;
        game.Arguments = compatibility ? ["--rendering-method", "gl_compatibility", "--rendering-driver", "opengl3"] : [];
        game.PresentationModeChanged += (_, _) => File.AppendAllText(log, $"Mode {game.ActivePresentationMode}\n");
        game.Failed += (_, error) => {
            File.AppendAllText(log, $"FAILED {error}\n");
            Environment.ExitCode = 1;
            Application.Current!.CloseWindow(Window);
        };
        game.Started += (_, _) => {
            File.AppendAllText(log, "Started\n");
            game.Dispatch(tree => {
                if (tree.CurrentScene is null) throw new Exception("Started fired before the main scene loaded.");
                if (tree.CurrentScene is not global::MauiSmoke.SmokeNode)
                    throw new Exception("The game script and host use different assembly load contexts.");
                File.AppendAllText(log, "READY_SCENE\n");
            });
            Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () => {
                Window.Width = 720;
                Window.Height = 480;
                return false;
            });
            Dispatcher.StartTimer(TimeSpan.FromSeconds(3), () => {
                game.Dispatch(tree => {
                    if (tree.Root.GetChildCount() == 0) throw new Exception("Scene did not load.");
                    tree.Paused = true;
                    if (!tree.Paused) throw new Exception("Game command did not execute.");
                    File.AppendAllText(log, $"PASSED {game.ActivePresentationMode}\n");
                    tree.Quit();
                });
                return false;
            });
        };
        game.QuitRequested += (_, _) => Application.Current!.CloseWindow(Window);
    }
}
'''


def run(command, cwd, **kwargs):
    result = subprocess.run(command, cwd=cwd, text=True, encoding="utf-8", errors="replace",
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=240, **kwargs)
    if result.returncode:
        raise RuntimeError(f"{command}\n{result.stdout}")
    return result


@unittest.skipUnless(sys.platform == "win32", "WinUI runtime tests require Windows")
class MauiWindows(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="2dog-maui-")
        cls.root = Path(cls.temp.name)
        run(["dotnet", "build", "twodog", "-c", "Release", "--nologo"], REPO)
        tool = REPO / "twodog/bin/Release/net10.0/2dog.dll"
        run(["dotnet", str(tool), "new", "MauiSmoke", str(cls.root), "--maui", "--no-restore", "-y"], REPO)
        # A private cache and public SDK source keep the checkout's fork SDK out of the test.
        feed = str(options.feed.resolve()).replace("&", "&amp;").replace('"', "&quot;")
        (cls.root / "NuGet.Config").write_text(
            '<configuration><config><add key="globalPackagesFolder" value="./.packages/"/></config>'
            '<packageSources><clear/>'
            f'<add key="packages" value="{feed}"/>'
            '<add key="nuget" value="https://api.nuget.org/v3/index.json"/>'
            '</packageSources></configuration>', encoding="utf-8")
        (cls.root / "MauiSmoke.maui/MainPage.cs").write_text(PAGE, encoding="utf-8")
        (cls.root / "SmokeNode.cs").write_text(
            'namespace MauiSmoke; public partial class SmokeNode : Godot.Node { }', encoding="utf-8")
        (cls.root / "main.tscn").write_text('''
[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://SmokeNode.cs" id="1"]
[node name="SmokeRoot" type="Node"]
script = ExtResource("1")
''', encoding="utf-8")
        (cls.root / "MauiSmoke.maui/MainPage.xaml").write_text('''
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:dog="clr-namespace:twodog.Maui;assembly=twodog.maui"
    x:Class="MauiSmoke.Maui.MainPage">
    <Grid>
        <dog:GodotView x:Name="Game" Project="MauiSmoke"/>
        <Label Text="MAUI overlay" BackgroundColor="Navy" VerticalOptions="Start"/>
    </Grid>
</ContentPage>
''', encoding="utf-8")
        run(["dotnet", "build", "MauiSmoke.maui", "-f", "net10.0-windows10.0.19041.0", "--nologo"], cls.root)
        cls.exe = cls.root / "MauiSmoke.maui/bin/Debug/net10.0-windows10.0.19041.0/win-x64/MauiSmoke.maui.exe"

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def check_mode(self, mode, expected, compatibility=False):
        log = self.root / f"{mode}.log"
        environment = dict(os.environ, TWODOG_MAUI_SMOKE_MODE=mode,
                           TWODOG_MAUI_SMOKE_LOG=str(log),
                           TWODOG_MAUI_SMOKE_COMPATIBILITY="1" if compatibility else "0")
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = subprocess.SW_HIDE
        result = subprocess.run([str(self.exe)], cwd=self.root, env=environment,
                                text=True, encoding="utf-8", errors="replace", startupinfo=startup,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=45)
        events = log.read_text(encoding="utf-8") if log.exists() else "No lifecycle events"
        diagnostics = f"{events}\n{result.stdout}"
        self.assertEqual(0, result.returncode, diagnostics)
        self.assertNotIn("FAILED", events, diagnostics)
        self.assertIn("READY_SCENE", events, diagnostics)
        self.assertIn(f"PASSED {expected}", events, diagnostics)
        self.assertIn("Godot instance destroyed", result.stdout, diagnostics)

    def test_cpu_resize_commands_and_shutdown(self):
        self.check_mode("Cpu", "Cpu")

    def test_auto_falls_back_for_compatibility_renderer(self):
        self.check_mode("Auto", "Cpu", compatibility=True)

    @unittest.skipUnless(options.require_gpu, "Pass --require-gpu on a Vulkan/shared-texture capable GPU")
    def test_gpu_resize_commands_and_shutdown(self):
        self.check_mode("Gpu", "Gpu")


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0], *remaining], verbosity=2)
