using System.Reflection;
using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit.v3;

namespace twodog.tests.EngineTests;

[Collection<HeadlessCollection>]
public class GodotErrorCaptureTests(HeadlessFixture godot)
{
    [Fact]
    public void PushedError_IsCaptured()
    {
        GD.PushError("expected pushed error");

        var error = Assert.Single(godot.Errors.Expect("expected pushed error"));
        Assert.Equal(GodotErrorType.Error, error.Type);
    }

    [Fact]
    public void PushedWarning_IsCapturedAsWarning()
    {
        GD.PushWarning("expected pushed warning");

        var warning = Assert.Single(godot.Errors.Expect("expected pushed warning"));
        Assert.Equal(GodotErrorType.Warning, warning.Type);
    }

    [Fact]
    public void ExceptionInDeferredCallback_IsCaptured()
    {
        Callable.From(() => throw new InvalidOperationException("expected callback exception")).CallDeferred();
        godot.Engine.Iteration();

        godot.Errors.Expect("expected callback exception");
    }

    [Fact]
    public async Task ErrorFromAnotherThread_IsCaptured()
    {
        await Task.Run(() => GD.PushError("expected worker error"), TestContext.Current.CancellationToken);

        godot.Errors.Expect("expected worker error");
    }

    [Fact]
    public void Expect_ThrowsWhenNothingWasReported()
    {
        Assert.Throws<GodotErrorException>(() => godot.Errors.Expect("never reported"));
    }

    [Fact]
    public void Expect_ThrowsOnAnUnexpectedError()
    {
        GD.PushError("expected but different");

        var failure = Assert.Throws<GodotErrorException>(() => godot.Errors.Expect("something else"));
        Assert.Contains("expected but different", failure.Message);
    }

    [Fact]
    public void FailOnGodotErrors_FailsATestThatLeavesAnErrorBehind()
    {
        var test = (IXunitTest)TestContext.Current.Test!;
        var method = (MethodInfo)MethodBase.GetCurrentMethod()!;
        GD.PushError("left behind on purpose");

        var failure = Assert.Throws<GodotErrorException>(() => new FailOnGodotErrorsAttribute().After(method, test));
        Assert.Contains("left behind on purpose", failure.Message);
        Assert.Empty(godot.Errors.Drain());
    }

    [Fact]
    public void FailOnGodotErrors_PassesATestWithoutErrors()
    {
        var test = (IXunitTest)TestContext.Current.Test!;
        var method = (MethodInfo)MethodBase.GetCurrentMethod()!;

        new FailOnGodotErrorsAttribute().After(method, test);
    }

    [Fact]
    [AllowGodotErrors]
    public void AllowGodotErrors_LetsATestLeaveErrorsUnchecked()
    {
        GD.PushError("allowed to stay unchecked");
    }
}
