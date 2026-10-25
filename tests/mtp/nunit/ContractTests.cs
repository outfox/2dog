using NUnit.Framework;
using twodog.Testing.NUnit;

namespace Mtp.NUnit;

public sealed class ContractTests : GodotTestFixture
{
    private int _ownerThreadId;

    private static void Record(string step)
    {
        var path = Environment.GetEnvironmentVariable("MTP_CONTRACT_LOG")!;
        File.AppendAllText(path, $"{step}|{Environment.CurrentManagedThreadId}\n");
    }

    [OneTimeSetUp]
    public void SetUpContract()
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
        Record("setup");
        EngineFixture.Engine.Exited += () => Record("teardown");
    }

    [OneTimeTearDown]
    public void TearDownContract()
    {
        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_ownerThreadId));
    }

    [Test]
    public async Task Passing()
    {
        await Task.Yield();
        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_ownerThreadId));
        Assert.That(EngineFixture.Engine.Update(), Is.True);
        Record("Passing");
    }

    [Test]
    public void Failing()
    {
        Record("Failing");
        Assert.That(1, Is.EqualTo(2));
    }

    [Test, Ignore("contract skip")]
    public void Skipped() => throw new InvalidOperationException("Skipped body ran");

    [TestCase(2, 4)]
    [TestCase(3, 6)]
    public async Task Parameterized(int value, int expected)
    {
        await Task.Yield();
        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_ownerThreadId));
        Assert.That(EngineFixture.Engine.Update(), Is.True);
        Record($"Parameterized:{value}");
        Assert.That(value * 2, Is.EqualTo(expected));
    }

    // NUnit owns this cooperative timeout; adapter session cancellation skips teardown.
    [Test, CancelAfter(1000)]
    public async Task Cancellation()
    {
        Record("Cancellation");
        if (Environment.GetEnvironmentVariable("MTP_CONTRACT_WAIT") == "1")
        {
            try { await Task.Delay(TimeSpan.FromSeconds(60), TestContext.CurrentContext.CancellationToken); }
            catch (OperationCanceledException) { Record("cancellation-observed"); throw; }
        }
    }

    [Test]
    public void NativeWarning()
    {
        Record("NativeWarning");
        global::Godot.GD.PushWarning("MTP native warning contract");
    }
}
