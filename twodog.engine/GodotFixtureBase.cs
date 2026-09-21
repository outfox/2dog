using System;

namespace twodog.fixture;

/// <summary>Legacy name of <see cref="twodog.Testing.FixtureBase"/>.</summary>
[Obsolete("Use twodog.Testing.FixtureBase instead.")]
public abstract class GodotFixtureBase : global::twodog.Testing.FixtureBase
{
    /// <summary>Starts an engine with the given Godot command-line arguments.</summary>
    protected GodotFixtureBase(params string[] cmdLineArgs) : base(cmdLineArgs) { }
}
