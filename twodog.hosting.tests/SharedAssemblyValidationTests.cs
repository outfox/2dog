using System.Reflection;

namespace twodog.Hosting.Tests;

/// <summary>Unit tests of the shared-assembly closure walk (internal), using a
/// controlled resolver so the unresolvable cases are deterministic.</summary>
public sealed class SharedAssemblyValidationTests
{
    private static Func<AssemblyName, string?> MapOnly(params (string Name, string Path)[] entries) =>
        name => entries.FirstOrDefault(e => string.Equals(e.Name, name.Name, StringComparison.OrdinalIgnoreCase)).Path;

    [Fact]
    public void NeverSharedRootIsRejectedBeforeResolution()
    {
        var e = Assert.Throws<ArgumentException>(() =>
            EngineHost.ValidateSharedAssemblies(["GodotSharp"], _ => null));
        Assert.Contains("per-instance", e.Message);
    }

    [Fact]
    public void UnresolvableRootIsRejected()
    {
        var e = Assert.Throws<ArgumentException>(() =>
            EngineHost.ValidateSharedAssemblies(["no.such.assembly.2dog"], _ => null));
        Assert.Contains("cannot be located for validation", e.Message);
    }

    [Fact]
    public void WrapperWithUnresolvableTransitiveReferenceIsRejected()
    {
        // xunit.v3.core references other non-framework xunit assemblies; with
        // only the root mapped, those transitive refs are unlocatable and must
        // fail closed (they could hide an engine-stack reference).
        var root = typeof(FactAttribute).Assembly;
        var rootName = root.GetName().Name!;
        var e = Assert.Throws<ArgumentException>(() =>
            EngineHost.ValidateSharedAssemblies([rootName], MapOnly((rootName, root.Location))));
        Assert.Contains("cannot be located for validation", e.Message);
    }

    [Fact]
    public void FrameworkOnlyClosurePasses()
    {
        // twodog.hosting references framework assemblies only; unlocatable
        // framework-named references are skipped (framework code cannot
        // reference user code).
        var root = typeof(EngineHost).Assembly;
        var rootName = root.GetName().Name!;
        EngineHost.ValidateSharedAssemblies([rootName], MapOnly((rootName, root.Location)));
    }

    [Fact]
    public void EngineReferenceBehindAFrameworkNameIsStillRejected()
    {
        // The framework-name skip only applies to what cannot be located: a
        // locatable assembly hiding behind a System.* name is walked like any
        // other, so its engine-stack reference is detected.
        var disguised = Path.Combine(AppContext.BaseDirectory, "twodog.dll");
        var e = Assert.Throws<ArgumentException>(() =>
            EngineHost.ValidateSharedAssemblies(["System.Sneaky"], MapOnly(("System.Sneaky", disguised))));
        Assert.Contains("per-instance", e.Message);
    }
}
