using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using twodog.generators;

namespace twodog.bindings.tests;

/// <summary>
/// Drives ExportSignalGenerator through a Roslyn GeneratorDriver against stub
/// Godot types - validates discovery, dedup across partials, member-contract
/// validation, and diagnostic shape without needing the engine.
/// </summary>
public class GeneratorDriverTests
{
    private const string Stubs = """
        namespace Godot
        {
            public enum PropertyHint { None = 0, Range = 1 }
            public class ExportAttribute : System.Attribute
            {
                public ExportAttribute(PropertyHint hint = PropertyHint.None, string hintString = "") { }
            }
            public class SignalAttribute : System.Attribute { }
            public class GodotObject { }
            public class Node : GodotObject { }
            public class Node2D : Node { }
            public class Resource : GodotObject { }
        }
        """;

    private static GeneratorRunResult Run(string source)
    {
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "netstandard.dll" or "System.Private.CoreLib.dll")
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var compilation = CSharpCompilation.Create("gentest",
            [CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText(source)],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new ExportSignalGenerator());
        return ((CSharpGeneratorDriver)driver.RunGenerators(compilation)).GetRunResult().Results.Single();
    }

    [Fact]
    public void SplitPartialClass_EmitsExactlyOnce_WithAllMembers()
    {
        var result = Run("""
            public partial class Split : Godot.Node
            {
                [Godot.Export] public int A { get; set; }
            }
            public partial class Split
            {
                [Godot.Export] public float B { get; set; }
                [Godot.Signal] public delegate void PingedEventHandler();
            }
            """);

        var source = Assert.Single(result.GeneratedSources);
        Assert.Contains("r.Property(\"a\"", source.SourceText.ToString());
        Assert.Contains("r.Property(\"b\"", source.SourceText.ToString());
        Assert.Contains("r.Signal(\"pinged\")", source.SourceText.ToString());
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void QualifiedAndAliasedAttributes_AreDiscovered()
    {
        var result = Run("""
            using GdExport = Godot.ExportAttribute;
            public partial class Aliased : Godot.Node
            {
                [GdExport] public int A { get; set; }
                [global::Godot.Export] public int B { get; set; }
            }
            """);

        var source = Assert.Single(result.GeneratedSources);
        Assert.Contains("r.Property(\"a\"", source.SourceText.ToString());
        Assert.Contains("r.Property(\"b\"", source.SourceText.ToString());
    }

    [Fact]
    public void UnsupportedOnlyClass_StillReportsDiagnostic_AndEmitsNothing()
    {
        var result = Run("""
            public partial class Orphan : Godot.Node
            {
                [Godot.Export] public System.DateTime When { get; set; }
            }
            """);

        Assert.Empty(result.GeneratedSources);
        var diag = Assert.Single(result.Diagnostics);
        Assert.Equal("TDOG001", diag.Id);
        Assert.NotEqual(Location.None, diag.Location);
    }

    [Fact]
    public void NonPartialClass_ReportsTDOG002()
    {
        var result = Run("""
            public class Sealed : Godot.Node
            {
                [Godot.Export] public int A { get; set; }
            }
            """);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "TDOG002");
    }

    [Fact]
    public void NestedClass_ReportsTDOG002()
    {
        var result = Run("""
            public partial class Outer
            {
                public partial class Inner : Godot.Node
                {
                    [Godot.Export] public int A { get; set; }
                }
            }
            """);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "TDOG002");
    }

    [Theory]
    [InlineData("public int A { get; }")]
    [InlineData("public int A { get; init; }")]
    [InlineData("public readonly int A;")]
    [InlineData("public const int A = 1;")]
    [InlineData("public static int A { get; set; }")]
    public void NonWritableOrStaticExport_ReportsTDOG004(string member)
    {
        var result = Run($$"""
            public partial class Bad : Godot.Node
            {
                [Godot.Export] {{member}}
            }
            """);

        Assert.Empty(result.GeneratedSources);
        var diag = Assert.Single(result.Diagnostics);
        Assert.Equal("TDOG004", diag.Id);
        Assert.NotEqual(Location.None, diag.Location);
    }

    [Theory]
    [InlineData("public delegate void Renamed(int x);", "TDOG003")]           // missing suffix
    [InlineData("public delegate int ScoredEventHandler();", "TDOG003")]      // non-void return
    [InlineData("public delegate void MovedEventHandler(ref int x);", "TDOG003")] // by-ref param
    [InlineData("public delegate void TimedEventHandler(System.DateTime t);", "TDOG001")] // unsupported param
    public void InvalidSignalDelegate_ReportsDiagnostic(string member, string expectedId)
    {
        var result = Run($$"""
            public partial class BadSignal : Godot.Node
            {
                [Godot.Signal] {{member}}
            }
            """);

        Assert.Empty(result.GeneratedSources);
        var diag = Assert.Single(result.Diagnostics);
        Assert.Equal(expectedId, diag.Id);
    }

    [Fact]
    public void NodeExport_CarriesClassNameAndNodeTypeHint()
    {
        var result = Run("""
            public partial class Holder : Godot.Node
            {
                [Godot.Export] public Godot.Node2D? Target { get; set; }
            }
            """);

        var text = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("34, \"Node2D\", 6, \"Node2D\"", text); // PropertyHint.NodeType + class_name
    }

    [Fact]
    public void ResourceExport_CarriesClassNameAndResourceTypeHint()
    {
        var result = Run("""
            public partial class Holder : Godot.Node
            {
                [Godot.Export] public Godot.Resource? Data { get; set; }
            }
            """);

        var text = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("17, \"Resource\", 6, \"Resource\"", text); // PropertyHint.ResourceType + class_name
    }

    [Fact]
    public void InternalSignalDelegate_GetsInternalEventAndEmitter()
    {
        var result = Run("""
            public partial class Quiet : Godot.Node
            {
                [Godot.Signal] internal delegate void HushedEventHandler();
            }
            """);

        var text = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("internal event HushedEventHandler Hushed", text);
        Assert.Contains("internal void EmitSignalHushed()", text);
    }
}
