using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using twodog;
using twodog.tests.EngineTests;

namespace twodog.tests.AvaloniaTests;

// Owns an engine: do not run alongside the shared engine fixtures.
[Collection(nameof(EngineRunCollection))]
public class GodotSessionPumpTests
{
    [Fact]
    public void CompositionTicks_AdvanceFramesEvenWithoutAnImageUpdate() =>
        GodotControlLifecycleTests.Dispatch(() =>
        {
            twodog.fixture.AssemblyPreloader.PreloadGameAssemblies(Engine.ResolveProjectDir());
            using var session = new GodotSession(new GodotSessionOptions
            {
                Project = "avalonia-composition-pump",
                ExtraArgs = ["--headless"],
                // No GPU import on the headless platform: presentation produces no commits.
                PresentationMode = GodotPresentationMode.Gpu,
            });
            var window = new Window { Content = new GodotControl { Session = session } };
            window.Show();
            try
            {
                var frames = 0;
                session.FrameAdvanced += (_, _) => frames++;
                session.Start();
                Dispatcher.UIThread.RunJobs();
                for (var i = 0; i < 5; i++)
                {
                    var before = frames;
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(frames > before, "The pump waited for the idle animation timer instead of the next commit.");
                }
            }
            finally
            {
                session.Dispose();
                window.Close();
            }
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NestedFrameCallback_DoesNotStopThePump(bool duringEngineFrame) =>
        GodotControlLifecycleTests.Dispatch(() =>
        {
            twodog.fixture.AssemblyPreloader.PreloadGameAssemblies(Engine.ResolveProjectDir());
            using var session = new GodotSession(new GodotSessionOptions
            {
                Project = "nested-avalonia-pump",
                ExtraArgs = ["--headless"],
            });
            session.Start();
            var tick = typeof(GodotSession)
                .GetMethod("OnCompositionUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action>(session);
            var nested = false;
            var frames = 0;
            void NestedTick()
            {
                if (nested) return;
                nested = true;
                tick();
            }

            if (duringEngineFrame) session.Engine.Tree.ProcessFrame += NestedTick;
            session.FrameAdvanced += (_, _) =>
            {
                frames++;
                if (!duringEngineFrame) NestedTick();
            };

            tick();
            Assert.True(nested);
            Assert.Equal(1, frames);
            tick();
            Assert.Equal(2, frames);
        });
}
