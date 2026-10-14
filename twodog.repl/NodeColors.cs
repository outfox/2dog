using System.Text;
using Godot;

namespace twodog.Repl;

internal enum NodeFamily { Node, Control, Node2D, Node3D, Animation }
internal sealed record NodePathStyle(string Path, IReadOnlyDictionary<string, NodeFamily> Families)
{
    public StyledText Display => NodeColors.Path(Path, Families);
}

internal static class NodeColors
{
    // Godot editor/icons/{Node,Control,Node2D,Node3D,AnimationPlayer}.svg.
    // Use RGB rather than terminal theme colors to retain the editor's palette.
    internal static readonly TermColor Grey = TermColor.Rgb(0x91, 0x91, 0x91);
    internal static TermColor Color(NodeFamily family) => family switch
    {
        NodeFamily.Control => TermColor.Rgb(0x8e, 0xef, 0x97),
        NodeFamily.Node2D => TermColor.Rgb(0x8d, 0xa5, 0xf3),
        NodeFamily.Node3D => TermColor.Rgb(0xfc, 0x7f, 0x7f),
        NodeFamily.Animation => TermColor.Rgb(0xc3, 0x8e, 0xf1),
        _ => TermColor.Rgb(0xe0, 0xe0, 0xe0),
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
    internal static StyledText Tree(Node node)
    {
        var text = node.GetTreeStringPretty();
        var spans = new List<StyledSpan>();
        var stack = new Stack<Node>();
        stack.Push(node);
        var offset = 0;
        while (stack.TryPop(out var current))
        {
            var name = current.Name.ToString();
            // Locate the node name at its actual line ending. Do not assume a
            // particular width for Godot's tree connectors or indentation.
            var nameStart = text.IndexOf(name + "\n", offset, StringComparison.Ordinal);
            spans.Add(new(offset, nameStart - offset, Grey));
            spans.Add(new(nameStart, name.Length, Color(Family(current))));
            offset = nameStart + name.Length + 1;
            // Native GetTreeStringPretty includes internal children too.
            var children = current.GetChildren(includeInternal: true);
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
        return new(text, spans);
    }

    internal static StyledText Path(string display, IReadOnlyDictionary<string, NodeFamily>? families = null)
    {
        var spans = new List<StyledSpan>();
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

}
