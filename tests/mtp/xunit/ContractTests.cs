using twodog.Testing;
using Xunit;

namespace Mtp.Xunit;

public sealed class ProbeFixture : HeadlessFixture, IDisposable
{
    public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;
    public ProbeFixture() => Record("setup");

    public static void Record(string step)
    {
        var path = Environment.GetEnvironmentVariable("MTP_CONTRACT_LOG")!;
        File.AppendAllText(path, $"{step}|{Environment.CurrentManagedThreadId}\n");
    }

    void IDisposable.Dispose()
    {
        base.Dispose();
        Engine.Completion.GetAwaiter().GetResult();
        Record("teardown");
    }
}

public sealed class ContractTests(ProbeFixture fixture) : IClassFixture<ProbeFixture>
{
    [Fact]
    public async Task Passing()
    {
        await Task.Yield();
        Assert.Equal(fixture.OwnerThreadId, Environment.CurrentManagedThreadId);
        Assert.False(fixture.Engine.Iteration());
        ProbeFixture.Record("Passing");
    }

    [Fact]
    public void Failing()
    {
        ProbeFixture.Record("Failing");
        Assert.Equal(2, 1);
    }

    [Fact(Skip = "contract skip")]
    public void Skipped() => throw new InvalidOperationException("Skipped body ran");

    [Theory]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    public async Task Parameterized(int value, int expected)
    {
        await Task.Yield();
        Assert.Equal(fixture.OwnerThreadId, Environment.CurrentManagedThreadId);
        Assert.False(fixture.Engine.Iteration());
        ProbeFixture.Record($"Parameterized:{value}");
        Assert.Equal(expected, value * 2);
    }

    [Fact]
    public async Task Cancellation()
    {
        ProbeFixture.Record("Cancellation");
        if (Environment.GetEnvironmentVariable("MTP_CONTRACT_WAIT") == "1")
        {
            try { await Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken); }
            catch (OperationCanceledException) { ProbeFixture.Record("cancellation-observed"); throw; }
        }
    }

    [Fact]
    public void NativeWarning()
    {
        ProbeFixture.Record("NativeWarning");
        Godot.GD.PushWarning("MTP native warning contract");
    }
}
