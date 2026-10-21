using System.Reflection;
using twodog;
using twodog.tests.EngineTests;

namespace twodog.tests.AvaloniaTests;

// Owns an engine: do not run alongside the shared engine fixtures.
[Collection(nameof(EngineRunCollection))]
public class GodotSessionPumpTests
{
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
                .GetMethod("OnAnimationFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<TimeSpan>>(session);
            var nested = false;
            var frames = 0;
            void NestedTick()
            {
                if (nested) return;
                nested = true;
                tick(TimeSpan.Zero);
            }

            if (duringEngineFrame) session.Engine.Tree.ProcessFrame += NestedTick;
            session.FrameAdvanced += (_, _) =>
            {
                frames++;
                if (!duringEngineFrame) NestedTick();
            };

            tick(TimeSpan.Zero);
            Assert.True(nested);
            Assert.Equal(1, frames);
            tick(TimeSpan.Zero);
            Assert.Equal(2, frames);
        });
}
