using System;
using System.Threading;
using NUnit.Framework;

namespace twodog.Testing.NUnit;

/// <summary>A rendering engine per sequential NUnit fixture. Requires a display; supports Windows and Linux.</summary>
[Apartment(ApartmentState.STA)]
public abstract class GodotRenderingTestFixture : GodotTestFixture
{
    internal sealed override void ValidateRunner()
    {
        if (OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "NUnit's standard runner does not reserve the process main thread for Godot rendering on macOS. " +
                "Use GodotTestFixture for headless tests or 2dog.xunit's RenderingCollection for rendering.");
    }

    /// <summary>Starts a rendering engine. Override to supply custom engine arguments.</summary>
    protected override FixtureBase CreateFixture() => new Fixture();
}
