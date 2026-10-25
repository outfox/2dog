using Godot;
using Engine = twodog.Engine;

internal static class CanvasViewportSmoke
{
    private static readonly Vector2I[] SamplePositions =
    [
        new(17, 17), new(36, 26), new(54, 34),
        new(14, 26), new(58, 26), new(36, 14), new(36, 38),
    ];

    public static void Run(Engine engine)
    {
        Vector2I[] sizes = [new(640, 360), new(128, 96), new(320, 240)];
        var viewports = new List<SubViewport>();
        var rectangles = new List<ColorRect>();
        var colors = new[] { Colors.Red, Colors.Lime, Colors.Blue };
        try
        {
            for (int i = 0; i < sizes.Length; i++)
            {
                var viewport = new SubViewport
                {
                    Size = sizes[i],
                    Disable3D = true,
                    TransparentBg = true,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                };
                engine.Tree.Root.AddChild(viewport);
                var rectangle = new ColorRect
                {
                    Position = new Vector2(16, 16),
                    Size = new Vector2(40, 20),
                    Color = colors[i],
                };
                viewport.AddChild(rectangle);
                viewports.Add(viewport);
                rectangles.Add(rectangle);
            }
            for (int frame = 0; frame < 6; frame++) Pump(engine);
            // Redraw one canvas at a time, as hovering a native editor popup does.
            for (int frame = 0; frame < 12; frame++)
            {
                if (frame == 6)
                {
                    // Popup windows can resize while other canvases retain their contents.
                    sizes = [new(160, 120), new(512, 288), new(96, 64)];
                    for (int i = 0; i < viewports.Count; i++)
                    {
                        viewports[i].Size = sizes[i];
                        viewports[i].RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
                    }
                    Pump(engine);
                    Pump(engine);
                }
                for (int i = 0; i < viewports.Count; i++)
                    viewports[i].RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                int active = frame % viewports.Count;
                colors[active] = colors[active] == Colors.Red ? Colors.Lime : Colors.Red;
                rectangles[active].Color = colors[active];
                viewports[active].RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                Pump(engine);
                for (int i = 0; i < viewports.Count; i++)
                {
                    using var image = viewports[i].GetTexture().GetImage();
                    if (image.GetSize() != sizes[i]) throw new Exception($"Viewport {i} changed size.");
                    // Check both corners, the center and the surrounding transparent border.
                    foreach (var position in SamplePositions)
                    {
                        var actual = image.GetPixelv(position);
                        bool inside = position.X >= 16 && position.X < 56 && position.Y >= 16 && position.Y < 36;
                        if (inside ? actual.IsEqualApprox(colors[i]) : actual.A <= 0.01) continue;
                        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
                        Directory.CreateDirectory(directory);
                        image.SavePng(Path.Combine(directory, $"canvas-{RenderingServer.GetCurrentRenderingDriverName()}-{i}-{frame}.png"));
                        throw new Exception($"Viewport {i} ({sizes[i]}) frame {frame}: pixel {position} is {actual}, expected {(inside ? colors[i] : Colors.Transparent)}.");
                    }
                }
            }
            Console.WriteLine("NATIVE_CANVAS_VIEWPORTS_SMOKE_PASSED");
        }
        finally
        {
            foreach (var viewport in viewports) viewport.Free();
        }
    }

    private static void Pump(Engine engine)
    {
        if (!engine.Update()) throw new Exception("Unexpected engine quit.");
        Thread.Sleep(20);
    }
}
