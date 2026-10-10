using Microsoft.CodeAnalysis.Text;
using twodog.Repl;

namespace twodog.tests.ReplTests;

public class NodePathTests
{
    [Theory]
    [InlineData("$Control", 1)]
    [InlineData("$Control + $Other", 2)]
    [InlineData("\"$Control\"", 0)]
    [InlineData("@\"$Control\"", 0)]
    [InlineData("// $Control\n42", 0)]
    [InlineData("/* $Control */ 42", 0)]
    [InlineData("$\"literal $Control {1}\"", 0)]
    [InlineData("$$\"\"\"{{1}} $Control\"\"\"", 0)]
    [InlineData("$\"{$Control.Name}\"", 1)]
    public void NodeSyntaxLeavesStringsAndCommentsAlone(string text, int count)
        => Assert.Equal(count, NodePathInput.Find(text).Count);

    [Theory]
    [InlineData("?EngineT", 1)]
    [InlineData("?", 1)]
    [InlineData("var node = ?EngineT", 1)]
    [InlineData("Console.Write(?EngineT)", 1)]
    [InlineData("node == ?EngineT", 1)]
    [InlineData("node != ?EngineT", 1)]
    [InlineData("node < ?EngineT", 1)]
    [InlineData("node <= ?EngineT", 1)]
    [InlineData("node > ?EngineT", 1)]
    [InlineData("node >= ?EngineT", 1)]
    [InlineData("node == null ? EngineT : other", 0)]
    [InlineData("ls ?EngineT", 1)]
    [InlineData("rm ?EngineT", 1)]
    [InlineData("cp ?EngineT root", 1)]
    [InlineData("mv source ?EngineT", 1)]
    [InlineData(":mv $Source ?EngineT", 1)]
    [InlineData("mv source EngineT?.Name", 0)]
    [InlineData("mv source true ? EngineT : other", 0)]
    [InlineData(":rm ?EngineT", 1)]
    [InlineData("true ? EngineT : other", 0)]
    [InlineData("true?EngineT:other", 0)]
    [InlineData("int? EngineT", 0)]
    [InlineData("EngineT?.Name", 0)]
    [InlineData("EngineT?[0]", 0)]
    [InlineData("EngineT ?? other", 0)]
    [InlineData("\"?EngineT\"", 0)]
    [InlineData("// ?EngineT\n42", 0)]
    [InlineData("$\"literal ?EngineT {1}\"", 0)]
    [InlineData("$\"{?EngineT.Name}\"", 1)]
    public void NodeSearchOnlyDecoratesExpressionStarts(string text, int count)
        => Assert.Equal(count, NodePathInput.Find(text).Count(p => p.Search));

    [Theory]
    [InlineData("ls $ContFlair", 8, "Cont")]
    [InlineData("ls $ControlFlair", 11, "Control")]
    [InlineData("$Control/Flair", 9, "Control/")]
    [InlineData("$[\"Control/My NFlair\"]", 15, "Control/My N")]
    [InlineData("?EngineTFlair", 8, "EngineT")]
    public void CompletionUsesOnlyTheNodePrefixBeforeTheCaret(string text, int caret, string path)
    {
        var reference = NodePathInput.CompletionAt(text, caret);
        Assert.NotNull(reference);
        Assert.Equal(path, reference.Path);
        Assert.Equal(caret, reference.Span.End);
    }

    [Fact]
    public void TypedTranslationMapsPositionsAfterMultiplePathsBackToUserInput()
    {
        const string text = "$Control.Size + $Other.Position";
        var nodes = new Dictionary<string, NodePathTarget>
        {
            ["Control"] = new("Control", "global::Godot.Control", "Godot.Control"),
            ["Other"] = new("Other", "global::Godot.Node2D", "Godot.Node2D"),
        };
        var input = new NodePathInput(text, nodes);
        Assert.Contains("(root.GetNode<global::Godot.Control>(\"Control\")).Size", input.Code);
        Assert.Contains("(root.GetNode<global::Godot.Node2D>(\"Other\")).Position", input.Code);
        Assert.DoesNotContain("$", input.Code);
        var generated = input.Code.IndexOf("Position", StringComparison.Ordinal);
        var original = text.IndexOf("Position", StringComparison.Ordinal);
        Assert.Equal(generated, input.ToGenerated(original));
        Assert.Equal(new TextSpan(original, 8), input.ToOriginal(new TextSpan(generated, 8)));
        Assert.Equal(text.Length, input.ToOriginal(input.Code.Length));
    }

    [Fact]
    public void QuotedPathsSupportPunctuationAndIncompleteInput()
    {
        var reference = Assert.Single(NodePathInput.Find("$[\"Control/My Node\"]"));
        Assert.Equal("Control/My Node", reference.Path);
        Assert.True(reference.Complete);
        Assert.False(Assert.Single(NodePathInput.Find("$[\"Control/My Node\"")).Complete);
        Assert.True(ReplSession.IsComplete("$Control.Size"));
        Assert.False(ReplSession.IsComplete("$[\"Control"));
    }
}
