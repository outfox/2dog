using twodog.Repl;

namespace twodog.tests.ReplTests;

public class ListTreeCommandTests
{
    [Theory]
    [InlineData("ls", null)]
    [InlineData("  ls  ", null)]
    [InlineData("ls;", null)]
    [InlineData("ls $Control", "Control")]
    [InlineData("ls $Control/Child;", "Control/Child")]
    [InlineData("ls $[\"Control/My Node\"]", "Control/My Node")]
    public void ListingAcceptsRootAndSingleNodePaths(string text, string? path)
    {
        Assert.True(ListTreeCommand.TryParse(text, out var command));
        Assert.Null(command.Error);
        Assert.Equal(path, command.Path);
        Assert.True(ReplSession.IsComplete(text));
    }

    [Theory]
    [InlineData("ls unknown")]
    [InlineData("ls $")]
    [InlineData("ls $A $B")]
    [InlineData("ls $A + $B")]
    public void ListingRejectsExtraArgumentsWithoutExecutingCode(string text)
    {
        Assert.True(ListTreeCommand.TryParse(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
    }

    [Theory]
    [InlineData("ls()")]
    [InlineData("ls.Count")]
    [InlineData("ls + 1")]
    [InlineData("ls = value;")]
    [InlineData("ls += 2; value = ls;")]
    [InlineData("ls (root)")]
    [InlineData("ls ?? root")]
    [InlineData("ls + $Control.GetIndex()")]
    [InlineData("ls is Node")]
    [InlineData("var ls = 1;")]
    [InlineData("\"ls $Control\"")]
    public void OrdinaryCSharpKeepsItsMeaning(string text)
        => Assert.False(ListTreeCommand.TryParse(text, out _));

    [Theory]
    [InlineData("ls + 1")]
    [InlineData("ls = value;")]
    [InlineData("ls (root)")]
    public void ShellModeKeepsListingPrecedence(string text)
    {
        Assert.True(ReplMode.Shell.TryListing(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
        Assert.False(ReplMode.Auto.TryListing(text, out _));
        Assert.False(ReplMode.CSharp.TryListing(text, out _));
    }
}
