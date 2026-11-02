using System.Reflection;
using System.Reflection.Emit;

namespace twodog.tests.EngineTests;

// TwoDogLinkedLibGodot is written into the host project's assembly, not twodog.dll. The loader must therefore read
// the entry assembly, or a static-link host still probes for a shared library and fails with DllNotFoundException.
public class LinkedLibGodotTests
{
    private static Type Loader => typeof(Engine).Assembly.GetType("twodog.LibGodotLoader", throwOnError: true)!;

    private static MethodInfo Declares => Loader.GetMethod("DeclaresLinkedLibGodot",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private static MethodInfo Uses => Loader.GetMethod("UsesLinkedLibGodot",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Assembly WithMetadata(string key, string value)
    {
        // The attribute is compiler-emitted, so the stand-in assembly is emitted with it rather than declared.
        // Collectible, so the scan in UsesLinkedLibGodot does not see it for the rest of the test run.
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"LinkedHost{key}{value}"),
            AssemblyBuilderAccess.RunAndCollect);
        var attribute = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(attribute, [key, value]));
        return assembly;
    }

    [Fact]
    public void HostAssemblyMetadata_IsRead()
    {
        Assert.True((bool)Declares.Invoke(null, [WithMetadata("TwoDogLinkedLibGodot", "true")])!);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("yes")]
    public void OtherValues_DoNotLink(string value)
    {
        Assert.False((bool)Declares.Invoke(null, [WithMetadata("TwoDogLinkedLibGodot", value)])!);
    }

    [Fact]
    public void UnrelatedMetadata_IsIgnored()
    {
        // TwoDogVariant is also assembly metadata; reading the key must not match it.
        Assert.False((bool)Declares.Invoke(null, [WithMetadata("TwoDogVariant", "release")])!);
    }

    [Fact]
    public void TestHost_IsNotLinked()
    {
        // The test host references twodog.engine normally, so nothing in the loader may claim static linkage.
        Assert.False((bool)Declares.Invoke(null, [typeof(Engine).Assembly])!);
        Assert.False((bool)Uses.Invoke(null, [])!);
    }

    [Fact]
    public void EmittedAssemblies_DoNotClaimLinkage()
    {
        // The attribute is read from the entry assembly and, failing that, from loaded ones. A runtime-emitted
        // assembly is never the program image, so it must not turn every later resolve into a program-image lookup
        // for the rest of the process.
        WithMetadata("TwoDogLinkedLibGodot", "true");
        Assert.False((bool)Uses.Invoke(null, [])!);
    }
}
