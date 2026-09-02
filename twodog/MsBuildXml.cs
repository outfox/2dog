using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace twodog.cli;

/// <summary>Round-trips MSBuild files edited as XDocuments: whitespace, newline flavour and declaration all survive.</summary>
internal static class MsBuildXml
{
    public static XDocument Load(string path) => XDocument.Load(path, LoadOptions.PreserveWhitespace);

    /// <summary>The trimmed value of the first element with that local name, anywhere in the document, or null.</summary>
    public static string? Property(XContainer doc, string name) => doc.Descendants()
        .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    /// <summary>
    /// Appends a commented PropertyGroup carrying its own indentation, so the rest of a whitespace-preserved file
    /// stays byte-identical. Returns the group.
    /// </summary>
    public static XElement AppendPropertyGroup(XDocument doc, string comment, IEnumerable<XElement> children, string? label = null)
    {
        var root = doc.Root ?? throw new ToolException("not a valid MSBuild file");
        var group = new XElement(root.Name.Namespace + "PropertyGroup");
        if (label != null) group.SetAttributeValue("Label", label);
        foreach (var child in children)
            group.Add(new XText("\n        "), child);
        group.Add(new XText("\n    "));
        root.Add(new XText("    "), new XComment($" {comment} "), new XText("\n    "), group, new XText("\n"));
        return group;
    }

    /// <summary>
    /// The document as text. XDocument.ToString would rewrite every newline to the platform's; this writer leaves
    /// them alone, so an LF file stays LF on Windows.
    /// </summary>
    public static string Serialize(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
            Indent = false,
        };
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, settings))
            doc.Save(writer);

        var text = builder.ToString();
        if (doc.Declaration == null) return text;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return doc.Declaration + newline + text;
    }

    private static readonly Regex DeclaredEncoding =
        new(@"^\s*<\?xml[^>]*\bencoding\s*=\s*(?<q>[""'])(?<name>[^""']+)\k<q>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Writes edited MSBuild text back in the file's own encoding: the one its declaration names, else UTF-8 with
    /// the BOM the file had. File.WriteAllText would emit UTF-8 under a utf-16 declaration, which MSBuild rejects.
    /// </summary>
    public static void Write(string path, string text) => File.WriteAllText(path, text, EncodingOf(path, text));

    internal static Encoding EncodingOf(string path, string text)
    {
        var declared = DeclaredEncoding.Match(text);
        if (declared.Success)
        {
            try
            {
                var encoding = Encoding.GetEncoding(declared.Groups["name"].Value);
                if (encoding.CodePage != 65001) return encoding;
            }
            catch (ArgumentException) { /* unknown name: MSBuild would not read it either; fall through to UTF-8 */ }
        }

        return new UTF8Encoding(HasUtf8Bom(path));
    }

    private static bool HasUtf8Bom(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[3];
        return stream.Read(head) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }
}
