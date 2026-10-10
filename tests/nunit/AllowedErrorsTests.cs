using Godot;
using NUnit.Framework;
using twodog.Testing.NUnit;

namespace twodog.nunit.tests;

public class AllowedErrorsTests : GodotTestFixture
{
    [Test]
    [AllowGodotErrors]
    public void MethodAllowsErrorsThroughDerivedTeardown() => GD.PushError("allowed NUnit method error");

    [TearDown]
    public void Cleanup() => GD.PushWarning("allowed NUnit teardown warning");
}

[AllowGodotErrors]
public class AllowedFixtureErrorsTests : GodotTestFixture
{
    [TestCase(1)]
    [TestCase(2)]
    public void ParameterizedMethodInheritsFixtureAllowance(int _) => GD.PushError("allowed NUnit fixture error");

    [OneTimeTearDown]
    public void Cleanup() => GD.PushWarning("allowed NUnit one-time teardown warning");
}
