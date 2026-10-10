using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

[CollectionDefinition("Fixture disposal", DisableParallelization = true)]
public class FixtureDisposalCollection;

[Collection("Fixture disposal")]
public class FixtureDisposalTests
{
    [Fact]
    public async Task SwitchingDisplayModesFailsBeforeNativeStartup()
    {
        await GodotTestThread.Run(() =>
        {
            using (var headless = new HeadlessFixture()) { }
            var failure = Assert.Throws<InvalidOperationException>(() => new Fixture());
            Assert.Contains("separate test projects", failure.Message);
            using var restarted = new HeadlessFixture();
            return new ValueTask<int>(0);
        });
    }

    [Fact]
    public async Task CleanupAndShutdownErrorsAreReportedAfterTheEngineIsReleased()
    {
        await GodotTestThread.Run(() =>
        {
            var fixture = new HeadlessFixture { CheckErrorsOnDispose = true };
            GD.PushWarning("expected fixture cleanup warning");
            var node = new Node();
            fixture.Tree.Root.AddChild(node);
            node.TreeExiting += () => GD.PushError("expected engine shutdown error");
            var failure = Assert.Throws<GodotErrorException>(fixture.Dispose);
            Assert.Contains("expected fixture cleanup warning", failure.Message);
            Assert.Contains("expected engine shutdown error", failure.Message);
            fixture.Dispose();
            using var restarted = new HeadlessFixture { CheckErrorsOnDispose = true };
            return new ValueTask<int>(0);
        });
    }

    [Fact]
    public async Task OptOutPreservesShutdownErrorsForInspection()
    {
        await GodotTestThread.Run(() =>
        {
            var fixture = new HeadlessFixture();
            var node = new Node();
            fixture.Tree.Root.AddChild(node);
            node.TreeExiting += () => GD.PushWarning("expected opted-out shutdown warning");
            fixture.Dispose();
            Assert.Single(fixture.Errors.Expect("expected opted-out shutdown warning"));
            return new ValueTask<int>(0);
        });
    }
}
