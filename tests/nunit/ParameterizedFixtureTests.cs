using System.Globalization;
using NUnit.Framework;
using twodog.Testing;
using twodog.Testing.NUnit;
using GodotEngine = Godot.Engine;

namespace twodog.nunit.tests;

[TestFixture(30)]
[TestFixture(60)]
public class ParameterizedFixtureTests(int maxFps) : GodotTestFixture
{
    protected override FixtureBase CreateFixture() => new ConfiguredFixture(maxFps);

    [Test]
    public async Task EachConfigurationHasItsOwnEngine()
    {
        var owner = Environment.CurrentManagedThreadId;
        await Task.Yield();
        EngineFixture.Engine.Iteration();
        Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
        Assert.That(GodotEngine.MaxFps, Is.EqualTo(maxFps));
    }

    private sealed class ConfiguredFixture(int fps)
        : FixtureBase("--headless", "--max-fps", fps.ToString(CultureInfo.InvariantCulture));
}
