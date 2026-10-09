using System.Text;
using Godot;
using PrettyPrompt.Highlighting;

namespace twodog.Repl;

internal enum NodeFamily { Node, Control, Node2D, Node3D, Animation }
internal sealed record NodePathStyle(string Path, IReadOnlyDictionary<string, NodeFamily> Families)
{
    public FormattedString Display => NodeColors.Path(Path, Families);
}

internal static class NodeColors
{
    // Godot editor/icons/{Node,Control,Node2D,Node3D,AnimationPlayer}.svg.
    // Use RGB rather than terminal theme colors to retain the editor's palette.
    internal static readonly AnsiColor Grey = AnsiColor.Rgb(0x91, 0x91, 0x91);
    internal static AnsiColor Color(NodeFamily family) => family switch
    {
        NodeFamily.Control => AnsiColor.Rgb(0x8e, 0xef, 0x97),
        NodeFamily.Node2D => AnsiColor.Rgb(0x8d, 0xa5, 0xf3),
        NodeFamily.Node3D => AnsiColor.Rgb(0xfc, 0x7f, 0x7f),
        NodeFamily.Animation => AnsiColor.Rgb(0xc3, 0x8e, 0xf1),
        _ => AnsiColor.Rgb(0xe0, 0xe0, 0xe0),
    };

    // Only call on Godot's owner thread. Custom scripts inherit their native family.
    internal static NodeFamily Family(Node node) => node switch
    {
        Control => NodeFamily.Control,
        Node2D => NodeFamily.Node2D,
        Node3D => NodeFamily.Node3D,
        AnimationMixer => NodeFamily.Animation,
        _ => node.GetClass().ToString().StartsWith("Animation", StringComparison.Ordinal) ? NodeFamily.Animation : NodeFamily.Node,
    };

    internal static NodePathStyle CapturePath(Node node)
    {
        using var path = node.GetPath();
        var families = new Dictionary<string, NodeFamily>(StringComparer.Ordinal);
        for (Node? current = node; current is not null; current = current.GetParent())
        {
            using var ancestor = current.GetPath();
            families[ancestor.ToString()] = Family(current);
        }
        return new(path.ToString(), families);
    }

    // Snapshot formatting on the owner thread; terminal rendering never touches nodes.
    internal static FormattedString Tree(Node node)
    {
        var text = node.GetTreeStringPretty();
        var spans = new List<FormatSpan>();
        var stack = new Stack<(Node Node, int Depth)>();
        stack.Push((node, 0));
        var offset = 0;
        while (stack.TryPop(out var entry))
        {
            var prefix = 3 * (entry.Depth + 1);
            var name = entry.Node.Name.ToString();
            spans.Add(new(offset, prefix, Grey));
            spans.Add(new(offset + prefix, name.Length, Color(Family(entry.Node))));
            offset += prefix + name.Length + 1;
            // Native GetTreeStringPretty includes internal children too.
            var children = entry.Node.GetChildren(includeInternal: true);
            for (var i = children.Count - 1; i >= 0; i--) stack.Push((children[i], entry.Depth + 1));
        }
        return new(text, spans);
    }

    internal static FormattedString Path(string display, IReadOnlyDictionary<string, NodeFamily>? families = null)
    {
        var spans = new List<FormatSpan>();
        var start = display.StartsWith('$') || display.StartsWith('?') ? 1 : 0;
        var end = display.Length;
        var quoted = display.StartsWith("$[", StringComparison.Ordinal);
        if (quoted)
        {
            var quote = display.IndexOf('"', 2);
            if (quote >= 0)
            {
                start = quote + 1;
                var closing = display.LastIndexOf('"');
                if (closing > quote) end = closing;
            }
        }
        if (start > 0) spans.Add(new(0, start, Grey));
        var prefix = "";
        for (var position = start; position < end;)
        {
            if (display[position] == '/')
            {
                spans.Add(new(position++, 1, Grey));
                prefix += "/";
                continue;
            }
            var next = display.IndexOf('/', position, end - position);
            if (next < 0) next = end;
            var name = display[position..next];
            prefix += quoted ? Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseToken("\"" + name + "\"").ValueText : name;
            var family = families is not null && families.TryGetValue(prefix, out var known) ? known : NodeFamily.Node;
            spans.Add(new(position, next - position, Color(family)));
            position = next;
        }
        if (end < display.Length) spans.Add(new(end, display.Length - end, Grey));
        return new(display, spans);
    }

    internal static string Ansi(FormattedString text, bool color)
    {
        if (!color) return text.Text ?? "";
        var result = new StringBuilder();
        var position = 0;
        foreach (var span in text.FormatSpans)
        {
            result.Append(text.Text.AsSpan(position, span.Start - position));
            if (span.Formatting.Foreground is { } foreground) result.Append(foreground.GetEscapeSequence());
            result.Append(text.Text.AsSpan(span.Start, span.Length)).Append("\x1b[0m");
            position = span.End;
        }
        return result.Append(text.Text.AsSpan(position)).ToString();
    }
}
