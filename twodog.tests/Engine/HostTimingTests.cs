#if GODOT4_8_OR_GREATER
using twodog.fixture;
using NativeEngine = Godot.Engine;

namespace twodog.tests.EngineTests;

[Collection(nameof(EngineRunCollection))]
public class HostTimingTests
{
    [Fact]
    public void TimedFramesDeliverDoubleDeltasAndBoundFixedTicks()
    {
        var project = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(project);
        using var engine = new Engine("host-timing", project, "--headless", "--audio-driver", "Dummy", "--fixed-fps", "8");
        engine.Start();
        NativeEngine.PhysicsTicksPerSecond = 64;
        NativeEngine.MaxPhysicsStepsPerFrame = 3;
        var tree = engine.Tree;
        var probe = new showcase.ToolNode();
        tree.Root.AddChild(probe);
        var physics = new List<double>();
        tree.PhysicsFrame += () => physics.Add(tree.Root.GetPhysicsProcessDeltaTime());

        const double tick = 1.0 / 64;
        void Step(double seconds, int ticks, double phase)
        {
            int framesBefore = probe.ProcessCount;
            physics.Clear();
            Assert.True(engine.Update(seconds));
            Assert.Equal(framesBefore + 1, probe.ProcessCount);
            Assert.Equal(seconds * NativeEngine.TimeScale, probe.LastProcessDelta);
            Assert.Equal(ticks, physics.Count);
            Assert.All(physics, delta => Assert.Equal(tick * NativeEngine.TimeScale, delta));
            Assert.Equal(phase, NativeEngine.GetPhysicsInterpolationFraction());
        }

        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.Update(invalid));
        Assert.Equal(0, probe.ProcessCount);
        Step(0, 0, 0);
        Step(tick / 2, 0, 0.5);
        Step(tick / 2, 1, 0);
        Step(3 * tick, 3, 0);
        NativeEngine.TimeScale = 0.5;
        Step(17.5 * tick, 3, 0.5);
        Step(tick / 2, 1, 0);
        NativeEngine.TimeScale = 1;
        double small = Math.ScaleB(1.0, -36);
        Step(tick + small, 1, small * 64);
        Step(small, 0, small * 128);
        Assert.True(engine.Update());
        Assert.Equal(1.0 / 8, probe.LastProcessDelta, 10);
        engine.RequestQuit();
        Assert.False(engine.Update(0));
    }

    [Fact]
    public void MixedCadencesKeepPhysicsInPhaseWithHostTimestamps()
    {
        var project = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(project);
        using var engine = new Engine("host-clock", project, "--headless", "--audio-driver", "Dummy");
        engine.Start();
        NativeEngine.PhysicsTicksPerSecond = 60;
        ulong firstTick = NativeEngine.GetPhysicsFrames();
        const long origin = 1L << 60;
        long previous = origin;
        for (int frame = 1; frame <= 1440; frame++)
        {
            long now = origin + frame / 2 * 16_667L + (frame + 1) / 2 * 6_944L;
            Assert.True(engine.Update((now - previous) / 1_000_000.0));
            double observed = NativeEngine.GetPhysicsFrames() - firstTick + NativeEngine.GetPhysicsInterpolationFraction();
            double expected = (now - origin) * 60 / 1_000_000.0;
            Assert.Equal(expected, observed, 10);
            previous = now;
        }
    }
}
#endif
