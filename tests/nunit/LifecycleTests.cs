using NUnit.Framework;
using twodog.Testing;
using twodog.Testing.NUnit;

namespace twodog.nunit.tests;

public class LifecycleTests : GodotTestFixture
{
    private int _owner;

    protected override FixtureBase CreateFixture()
    {
        _owner = Environment.CurrentManagedThreadId;
        return base.CreateFixture();
    }

    private async Task CheckThreadAsync()
    {
        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_owner));
        await Task.Yield();
        await Task.Delay(1);
        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_owner));
        Godot.Engine.Iteration();
    }

    [OneTimeSetUp]
    public Task DerivedOneTimeSetUp() => CheckThreadAsync();

    [SetUp]
    public Task DerivedSetUp() => CheckThreadAsync();

    [TestCase(1)]
    [TestCase(2)]
    public Task TestsAndParameterizedCasesUseTheOwnerThread(int _) => CheckThreadAsync();

    [TearDown]
    public Task DerivedTearDown() => CheckThreadAsync();

    [OneTimeTearDown]
    public Task DerivedOneTimeTearDown() => CheckThreadAsync();
}
