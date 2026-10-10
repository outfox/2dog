using Godot;
using NUnit.Framework;
using twodog.Testing;
using twodog.Testing.NUnit;

namespace twodog.nunit.tests;

[SingleThreaded]
[NonParallelizable]
public class FixtureErrorTests
{
    [Test]
    public void ShutdownErrorsFailAndStillAllowRestart()
    {
        var fixture = new Harness();
        fixture.StartGodot();
        var node = new Node();
        fixture.Root.AddChild(node);
        node.TreeExiting += () => GD.PushWarning("NUnit expected shutdown warning");
        var failure = Assert.Throws<AssertionException>(fixture.StopGodot);
        Assert.That(failure!.Message, Does.Contain("NUnit expected shutdown warning"));
        fixture.StartGodot();
        fixture.StopGodot();
    }

    [Test]
    public void FinalTeardownErrorsFailAndStillDisposeTheEngine()
    {
        var fixture = new Harness();
        fixture.StartGodot();
        try
        {
            GD.PushWarning("NUnit expected final teardown warning");
            var failure = Assert.Throws<AssertionException>(fixture.StopGodot);
            Assert.That(failure!.Message, Does.Contain("NUnit expected final teardown warning"));
        }
        finally { fixture.StopGodot(); }

        // Restarting proves the failing teardown released the active engine.
        fixture.StartGodot();
        fixture.StopGodot();
    }

    [Test]
    public void OptOutPreservesErrorsAcrossTestBoundaries()
    {
        var fixture = new Harness { CheckErrors = false };
        fixture.StartGodot();
        try
        {
            GD.PushError("NUnit opted-out error");
            fixture.CheckGodotBeforeTest();
            fixture.CheckGodotAfterTest();
            Assert.That(fixture.Errors.Expect("NUnit opted-out error"), Has.Length.EqualTo(1));
        }
        finally { fixture.StopGodot(); }
    }

    private sealed class Harness : GodotTestFixture
    {
        public bool CheckErrors { get; init; } = true;
        protected override bool FailOnGodotErrors => CheckErrors;
        public GodotErrorLog Errors => Godot.Errors;
        public Node Root => EngineFixture.Tree.Root;
    }
}
