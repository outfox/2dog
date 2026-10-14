using System.Globalization;
using System.Text.Json;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Requests;

namespace twodog.GdUnit;

internal sealed class GdUnitTestCase
{
    public required string Guid { get; init; }
    public required string Uid { get; init; }
    public required string DisplayName { get; init; }
    public required string SourceFile { get; init; }
    public required int Line { get; init; }
    public required string FullyQualifiedName { get; init; }

    public static GdUnitTestCase From(JsonElement test, StagedProject project)
    {
        var resource = test.GetProperty("source_file").GetString()!;
        var method = test.GetProperty("test_name").GetString()!;
        var row = test.GetProperty("attribute_index").GetInt32().ToString(CultureInfo.InvariantCulture);
        var suite = test.GetProperty("managed_type").GetString()!;
        var display = test.GetProperty("simple_name").GetString()!;
        return new GdUnitTestCase
        {
            Guid = test.GetProperty("guid").GetString()!,
            // Native discovery GUIDs are random. Resource, method and row remain stable across stages and machines.
            Uid = $"gdunit:{Uri.EscapeDataString(resource)}:{Uri.EscapeDataString(method)}:{row}",
            DisplayName = $"{suite}.{display}",
            SourceFile = project.OriginalFile(resource),
            Line = test.GetProperty("line_number").GetInt32(),
            FullyQualifiedName = test.GetProperty("fully_qualified_name").GetString()!
        };
    }

    public TestNode Node(TestNodeStateProperty state, params IProperty[] extra)
    {
        var properties = new PropertyBag(state,
            new TestFileLocationProperty(SourceFile, new LinePositionSpan(new LinePosition(Line, 1), new LinePosition(Line, 1))),
            new TestMetadataProperty("Language", "GDScript"));
        foreach (var property in extra) properties.Add(property);
        return new TestNode { Uid = new TestNodeUid(Uid), DisplayName = DisplayName, Properties = properties };
    }

    public bool Matches(ITestExecutionFilter filter) => filter switch
    {
        NopFilter => true,
        TestNodeUidListFilter ids => ids.TestNodeUids.Any(id => id.Value == Uid),
        _ => throw new NotSupportedException($"Unsupported gdUnit test filter: {filter.GetType().Name}")
    };
}
