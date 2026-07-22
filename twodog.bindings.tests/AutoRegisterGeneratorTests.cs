using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using twodog.generators;

namespace twodog.bindings.tests;

/// <summary>
/// Drives AutoRegisterGenerator through a Roslyn GeneratorDriver against stub
/// Godot types - validates eligibility rules, base-before-derived ordering,
/// [SkipAutoRegister] propagation, and the TDOG005 diagnostic.
/// </summary>
public class AutoRegisterGeneratorTests
{
    private const string Stubs = """
        namespace Godot
        {
            public class SkipAutoRegisterAttribute : System.Attribute { }
            public class GodotObject { }
            public class Node : GodotObject { }
            public class Node2D : Node { }
            public class Resource : GodotObject { }
            public class Helper { }
        }
        namespace Godot.NativeInterop
        {
            public static class ClassRegistry
            {
                public static void Register<T>() where T : GodotObject, new() { }
            }
        }
        """;

    private static (string? Source, GeneratorRunResult Result) Run(string source, bool withRegistry = true)
    {
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "netstandard.dll" or "System.Private.CoreLib.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var stubs = withRegistry
            ? Stubs
            : Stubs.Replace("public static class ClassRegistry", "public static class NotTheRegistry");
        var compilation = CSharpCompilation.Create("gentest",
            [CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(source)],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new AutoRegisterGenerator());
        var result = ((CSharpGeneratorDriver)driver.RunGenerators(compilation)).GetRunResult().Results.Single();
        var generated = result.GeneratedSources.SingleOrDefault(s => s.HintName == "TwoDogAutoRegister.g.cs");
        return (generated.SourceText?.ToString(), result);
    }

    private static int RegisterIndex(string source, string fqn) =>
        source.IndexOf($"Register<{fqn}>()", StringComparison.Ordinal);

    [Fact]
    public void RegistersEligibleClasses_BaseBeforeDerived_RegardlessOfSourceOrder()
    {
        var (source, result) = Run("""
            public class Child : Base { }
            public class Base : Godot.Node { }
            public class Plain : Godot.Node2D { }
            """);

        Assert.NotNull(source);
        Assert.Empty(result.Diagnostics);
        var b = RegisterIndex(source!, "global::Base");
        var c = RegisterIndex(source!, "global::Child");
        Assert.True(b >= 0 && c >= 0 && b < c, "base must register before derived");
        Assert.True(RegisterIndex(source!, "global::Plain") >= 0);
        Assert.Contains("ModuleInitializer", source);
    }

    [Fact]
    public void NonPartialClassesAreRegistered_RegistrationNeedsNoGeneratedMembers()
    {
        var (source, _) = Run("public class NoAttributes : Godot.Node { }");
        Assert.NotNull(source);
        Assert.True(RegisterIndex(source!, "global::NoAttributes") >= 0);
    }

    [Fact]
    public void SkipAttribute_ExcludesClassAndItsDescendants()
    {
        var (source, result) = Run("""
            [Godot.SkipAutoRegister] public class Skipped : Godot.Node { }
            public class ChildOfSkipped : Skipped { }
            public class Kept : Godot.Node { }
            """);

        Assert.NotNull(source);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(-1, RegisterIndex(source!, "global::Skipped"));
        Assert.Equal(-1, RegisterIndex(source!, "global::ChildOfSkipped"));
        Assert.True(RegisterIndex(source!, "global::Kept") >= 0);
    }

    [Fact]
    public void IneligibleShapes_AreExcluded()
    {
        var (source, _) = Run("""
            public abstract class AbstractNode : Godot.Node { }
            public class GenericNode<T> : Godot.Node { }
            public class NoPublicCtor : Godot.Node { private NoPublicCtor() { } }
            public class NotGodot : Godot.Helper { }
            public class Outer { public class Nested : Godot.Node { } }
            public class Kept : Godot.Resource { }
            """);

        Assert.NotNull(source);
        Assert.Equal(-1, RegisterIndex(source!, "global::AbstractNode"));
        Assert.DoesNotContain("GenericNode", source);
        Assert.Equal(-1, RegisterIndex(source!, "global::NoPublicCtor"));
        Assert.Equal(-1, RegisterIndex(source!, "global::NotGodot"));
        Assert.DoesNotContain("Nested", source);
        Assert.True(RegisterIndex(source!, "global::Kept") >= 0);
    }

    [Fact]
    public void UnregistrableUserBase_ReportsTdog005_AndSkipsDerived()
    {
        var (source, result) = Run("""
            public abstract class EnemyBase : Godot.Node { }
            public class Goblin : EnemyBase { }
            """);

        Assert.Null(source); // nothing eligible -> no file at all
        var diag = Assert.Single(result.Diagnostics);
        Assert.Equal("TDOG005", diag.Id);
        Assert.Contains("Goblin", diag.GetMessage());
        Assert.Contains("EnemyBase", diag.GetMessage());
    }

    [Fact]
    public void GodotNamespaceClasses_AreNeverRegistered()
    {
        var (source, _) = Run("""
            namespace Godot { public class SneakyEngineClass : Node { } }
            public class UserClass : Godot.Node { }
            """);

        Assert.NotNull(source);
        Assert.DoesNotContain("SneakyEngineClass", source);
        Assert.True(RegisterIndex(source!, "global::UserClass") >= 0);
    }

    [Fact]
    public void WithoutClassRegistryInCompilation_EmitsNothing()
    {
        var (source, result) = Run("public class UserClass : Godot.Node { }", withRegistry: false);
        Assert.Null(source);
        Assert.Empty(result.Diagnostics);
    }
}
