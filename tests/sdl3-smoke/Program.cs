using Godot;
using Engine = twodog.Engine;

// Checks the SDL3 platform port through two engine lifetimes: the second one only
// succeeds if the first released its Vulkan context and process handles.
internal static class Program
{
    private static readonly List<string> Failures = [];

    private static void Check(bool ok, string what)
    {
        if (!ok) Failures.Add(what);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        for (int lifetime = 0; lifetime < 2; lifetime++)
        {
            string[] options = ["--display-driver", "sdl3", "--audio-driver", "Dummy"];
            if (args.Contains("--headless")) options = [.. options, "--headless"];

            using var engine = new Engine("Sdl3Smoke", AppContext.BaseDirectory, options) { CaptureErrors = true };
            engine.Start();

            string expectedDisplay = args.Contains("--headless") ? "headless" : "sdl3";
            Check(DisplayServer.GetName() == expectedDisplay, $"display server is {DisplayServer.GetName()}, not {expectedDisplay}");
            Check(OS.HasFeature("sdl3"), "OS.HasFeature(\"sdl3\") is false");

            // Bias is minutes with east negative, so it stays within +/-14 hours.
            long bias = Time.GetTimeZoneFromSystem()["bias"].As<long>();
            Check(Math.Abs(bias) <= 14 * 60, $"time zone bias {bias} is out of range");

            ulong before = Time.GetTicksUsec();
            OS.DelayUsec(2000);
            Check(Time.GetTicksUsec() - before >= 1000, "DelayUsec did not advance the clock");

            if (!args.Contains("--headless"))
            {
                // A swap chain that never presented would leave this at zero.
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (Godot.Engine.GetFramesDrawn() < 3 && DateTime.UtcNow < deadline && engine.Update()) { }
                Check(Godot.Engine.GetFramesDrawn() >= 3, $"only {Godot.Engine.GetFramesDrawn()} frames were drawn");
            }

            if (!args.Contains("--headless"))
            {
                // The pixels that reach the viewport, not just a frame counter: a clear colour nothing else paints.
                RenderingServer.SetDefaultClearColor(new Color(1f, 0f, 0.5f));
                for (int i = 0; i < 5 && engine.Update(); i++) { }
                var image = ((SceneTree)Godot.Engine.GetMainLoop()).Root.GetTexture().GetImage();
                var pixel = image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2);
                Check(Math.Abs(pixel.R - 1f) < 0.05f && pixel.G < 0.05f && Math.Abs(pixel.B - 0.5f) < 0.05f, $"center pixel is {pixel}, expected the clear colour");
                var shot = System.Environment.GetEnvironmentVariable("SDL3_SMOKE_SCREENSHOT");
                if (!string.IsNullOrEmpty(shot)) image.SavePng(shot);
            }

            // where.exe / which exit 1 when they find nothing and 0 when they do.
            string finder = OperatingSystem.IsWindows() ? "where.exe" : "which";
            string known = OperatingSystem.IsWindows() ? "cmd.exe" : "sh";
            // The exit code is read before the process is released; the second run must not see the first's code.
            int code = OS.Execute(finder, ["sdl3-smoke-no-such-file"]);
            Check(code == 1, $"Execute returned {code}, expected 1");
            int code2 = OS.Execute(finder, [known]);
            Check(code2 == 0, $"second Execute returned {code2}, expected 0");

            // Piped execution: stdout comes back through SDL_ReadProcess.
            var output = new Godot.Collections.Array();
            int pipeCode = OS.Execute(finder, [known], output);
            Check(pipeCode == 0 && output.Count == 1 && output[0].AsString().Contains(known), $"piped Execute returned {pipeCode}, output [{string.Join(",", output)}]");

            engine.Dispose();
            engine.Completion.GetAwaiter().GetResult();

            var errors = engine.Errors.Drain();
            if (errors.Length != 0)
            {
                Console.Error.WriteLine(string.Join("\n", errors.Select(error => error.ToString())));
                return 1;
            }
        }

        if (Failures.Count != 0)
        {
            foreach (var failure in Failures) Console.Error.WriteLine($"SDL3_SMOKE_FAILED: {failure}");
            return 1;
        }

        Console.WriteLine("SDL3_SMOKE_PASSED");
        return 0;
    }
}
