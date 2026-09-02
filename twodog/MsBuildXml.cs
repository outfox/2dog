using System.Text;
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
}
