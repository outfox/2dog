using System;
using System.Linq;
using System.Reflection;
using twodog.Testing;
using Xunit;
using Xunit.v3;

namespace twodog.Testing.Xunit;

/// <summary>
/// Fails a test that runs on a 2dog fixture when Godot reported an error or warning the test did not consume with
/// <see cref="GodotErrorLog.Expect"/>. Applied to the whole test assembly unless TwoDogFailOnGodotErrors is false.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class FailOnGodotErrorsAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (FixtureOf(test) is { } fixture)
            ThrowIfAny(fixture.Errors, "before this test started (engine startup, or deferred work of an earlier test)");
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (FixtureOf(test) is not { } fixture) return;
        if (AllowsErrors(methodUnderTest)) fixture.Errors.Drain();
        else ThrowIfAny(fixture.Errors, "during this test");
    }

    private static void ThrowIfAny(GodotErrorLog log, string when)
    {
        var errors = log.Drain();
        if (errors.Length > 0) throw new GodotErrorException($"Godot reported {errors.Length} error(s) {when}:", errors);
    }

    // Tests without a 2dog fixture have no engine to ask, and touching Godot there would crash the process.
    private static FixtureBase? FixtureOf(IXunitTest test)
    {
        var fixtureTypes = test.TestCase.TestCollection.CollectionFixtureTypes.Concat(test.TestCase.TestClass.ClassFixtureTypes);
        foreach (var type in fixtureTypes.Where(type => type.IsAssignableTo(typeof(FixtureBase))))
        {
            // Collection and class fixtures are created before their tests run, so the lookup has already completed.
            var lookup = TestContext.Current.GetFixture(type);
            if (lookup.IsCompletedSuccessfully && lookup.Result is FixtureBase fixture) return fixture;
        }
        return null;
    }

    private static bool AllowsErrors(MethodInfo method)
        => method.IsDefined(typeof(AllowGodotErrorsAttribute)) || method.DeclaringType?.IsDefined(typeof(AllowGodotErrorsAttribute)) == true;
}

/// <summary>Lets a test or test class leave Godot errors and warnings unchecked. Prefer <see cref="GodotErrorLog.Expect"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowGodotErrorsAttribute : Attribute;
