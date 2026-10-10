using System;
using NUnit.Framework;

namespace twodog.Testing.NUnit;

/// <summary>Allows native reports from a test and its teardown. Prefer EngineFixture.Errors.Expect for known reports.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class AllowGodotErrorsAttribute() : PropertyAttribute(PropertyName, "true")
{
    internal const string PropertyName = "TwoDog.AllowGodotErrors";
}
