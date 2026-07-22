using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace twodog.generators;

/// <summary>
/// Emits a [ModuleInitializer] that calls ClassRegistry.Register&lt;T&gt;() for every
/// eligible user class deriving Godot.GodotObject, so classes register on assembly
/// load without hand-written calls (Register queues until the engine's SCENE init).
/// Eligible: top-level, non-abstract, non-generic, public parameterless ctor, not
/// [SkipAutoRegister] (which also excludes derived classes). Emission is ordered
/// base-before-derived because ClassRegistry requires registered parents; user
/// bases from other assemblies are re-registered first (Register is idempotent),
/// covering load-order races between assembly module initializers.
/// </summary>
[Generator]
public sealed class AutoRegisterGenerator : IIncrementalGenerator
{
    private sealed record Candidate(string Fqn, int Depth, string ExternalBases, string? DiagMessage, Location? DiagLocation);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 } cds
                    && !cds.Modifiers.Any(SyntaxKind.StaticKeyword)
                    && !cds.Modifiers.Any(SyntaxKind.AbstractKeyword),
                static (ctx, _) => Analyze(ctx))
            .Where(static c => c is not null)
            .Collect();

        // The generated code calls ClassRegistry; emit nothing in compilations
        // that don't reference the gdext bindings (defensive: the analyzer could
        // be attached to an unrelated project).
        var hasRegistry = context.CompilationProvider.Select(static (c, _) =>
            c.GetTypeByMetadataName("Godot.NativeInterop.ClassRegistry") is not null);

        context.RegisterSourceOutput(candidates.Combine(hasRegistry),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static Candidate? Analyze(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) is not { } symbol) return null;
        if (symbol.ContainingType is not null || symbol.IsAbstract || symbol.IsGenericType) return null;
        if (IsGodotNamespace(symbol)) return null;
        if (HasSkipAttribute(symbol)) return null;
        if (!HasPublicParameterlessCtor(symbol)) return null;

        // Walk the base chain: user classes until the first Godot-namespace type,
        // which must lead to GodotObject for this to be an extension class at all.
        var userBases = new List<INamedTypeSymbol>();
        var reachesGodotObject = false;
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            if (IsGodotNamespace(t))
            {
                for (; t is not null; t = t.BaseType)
                {
                    if (t.ToDisplayString() == "Godot.GodotObject") { reachesGodotObject = true; break; }
                }
                break;
            }
            userBases.Add(t);
        }
        if (!reachesGodotObject) return null;

        foreach (var b in userBases)
        {
            if (HasSkipAttribute(b)) return null;
            if (b.IsAbstract || b.IsGenericType || b.ContainingType is not null
                || b.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
                || !HasPublicParameterlessCtor(b))
            {
                return new Candidate(Fqn(symbol), userBases.Count, "",
                    $"'{symbol.Name}' cannot be auto-registered: base class '{b.Name}' is not registrable as a " +
                    "Godot extension class (abstract, generic, nested, non-public, or without a public " +
                    "parameterless constructor).",
                    symbol.Locations.FirstOrDefault());
            }
        }

        // Root-first external user bases: registered by their own assembly's
        // initializer too, but re-registering here guarantees base-before-derived
        // ordering in the pre-engine queue regardless of assembly load order.
        var externals = new List<string>();
        for (var i = userBases.Count - 1; i >= 0; i--)
        {
            var b = userBases[i];
            if (!SymbolEqualityComparer.Default.Equals(b.ContainingAssembly, symbol.ContainingAssembly)
                && b.DeclaredAccessibility == Accessibility.Public)
            {
                externals.Add(Fqn(b));
            }
        }

        return new Candidate(Fqn(symbol), userBases.Count, string.Join(";", externals), null, null);
    }

    private static bool IsGodotNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false, ContainingNamespace.IsGlobalNamespace: false }) ns = ns.ContainingNamespace;
        return ns is { IsGlobalNamespace: false, Name: "Godot" };
    }

    private static bool HasSkipAttribute(INamedTypeSymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Godot.SkipAutoRegisterAttribute");

    private static bool HasPublicParameterlessCtor(INamedTypeSymbol symbol) =>
        symbol.InstanceConstructors.Any(c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public);

    private static string Fqn(INamedTypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static readonly DiagnosticDescriptor UnregistrableBase = new("TDOG005",
        "Class cannot be auto-registered", "{0}", "twodog", DiagnosticSeverity.Warning, true);

    private static void Emit(SourceProductionContext spc, System.Collections.Immutable.ImmutableArray<Candidate?> candidates, bool hasRegistry)
    {
        if (!hasRegistry) return;

        // Dedupe (partial declarations analyze once per declaration) and order
        // deterministically: depth ascending guarantees base-before-derived.
        var ordered = candidates.OfType<Candidate>()
            .GroupBy(c => c.Fqn)
            .Select(g => g.First())
            .OrderBy(c => c.Depth)
            .ThenBy(c => c.Fqn, StringComparer.Ordinal)
            .ToList();

        var lines = new List<string>();
        var seen = new HashSet<string>();
        foreach (var c in ordered)
        {
            if (c.DiagMessage is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(UnregistrableBase, c.DiagLocation ?? Location.None, c.DiagMessage));
                continue;
            }
            foreach (var external in c.ExternalBases.Split(';'))
            {
                if (external.Length > 0 && seen.Add(external)) lines.Add(external);
            }
            if (seen.Add(c.Fqn)) lines.Add(c.Fqn);
        }
        if (lines.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by twodog.generators/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace twodog.generated;");
        sb.AppendLine();
        sb.AppendLine("internal static class TwoDogAutoRegister");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void RegisterAll()");
        sb.AppendLine("    {");
        foreach (var fqn in lines)
        {
            sb.AppendLine($"        Try(static () => global::Godot.NativeInterop.ClassRegistry.Register<{fqn}>());");
        }
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    // Per-class guard, matching ClassRegistry's deferred-flush semantics: one");
        sb.AppendLine("    // failing class (e.g. a ClassDB name clash) must not sink the rest.");
        sb.AppendLine("    private static void Try(global::System.Action register)");
        sb.AppendLine("    {");
        sb.AppendLine("        try { register(); }");
        sb.AppendLine("        catch (global::System.Exception e)");
        sb.AppendLine("        {");
        sb.AppendLine("            global::System.Console.Error.WriteLine($\"twodog: auto-registration failed: {e}\");");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("TwoDogAutoRegister.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}
