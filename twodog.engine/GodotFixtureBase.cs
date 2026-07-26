using System;

namespace twodog.fixture;

[Obsolete("Use twodog.Testing.FixtureBase instead.")]
public abstract class GodotFixtureBase : global::twodog.Testing.FixtureBase
{
    protected GodotFixtureBase(params string[] cmdLineArgs) : base(cmdLineArgs) { }
}
