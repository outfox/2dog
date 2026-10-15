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
    [InlineData(":ls", null)]
    [InlineData("  :ls  ", null)]
    [InlineData(":ls;", null)]
    [InlineData(":ls $Control", "Control")]
    [InlineData(":ls $Control/Child;", "Control/Child")]
    [InlineData(":ls $[\"Control/My Node\"]", "Control/My Node")]
    [InlineData("ls /root", "/root")]
    [InlineData("ls /root/Control/", "/root/Control")]
    public void ListingAcceptsRootAndSingleNodePaths(string text, string? path)
    {
        Assert.True(ListTreeCommand.TryParse(text, out var command));
        Assert.Null(command.Error);
        Assert.Equal(path, command.Path);
        Assert.True(ReplSession.IsComplete(text));
    }

    [Theory]
    [InlineData("ls -d 1", null, 1)]
    [InlineData("ls -d 2 $Control", "Control", 2)]
    [InlineData(":ls -d 0 /root", "/root", 0)]
    [InlineData("ls -d  3  $Control/Child;", "Control/Child", 3)]
    public void DepthLimitsTheListing(string text, string? path, int depth)
    {
        Assert.True(ListTreeCommand.TryParse(text, out var command));
        Assert.Null(command.Error);
        Assert.Equal((path, depth), (command.Path, command.Depth));
    }

    [Fact]
    public void ADepthAwaitsItsPathAfterWhitespace()
    {
        Assert.True(ListTreeCommand.TryParse("ls -d 1 ", out var command));
        Assert.True(command.AwaitingTarget);
        Assert.Equal("ls -d 1 ".Length, command.ArgumentStart);
    }

    [Theory]
    [InlineData("ls -d")]
    [InlineData("ls -d x")]
    [InlineData("ls -d 2x")]
    [InlineData("ls -d 99999999999")]
    public void MalformedDepthsShowTheUsage(string text)
    {
        Assert.True(ListTreeCommand.TryParse(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
    }

    [Theory]
    [InlineData("ls unknown")]
    [InlineData("ls $")]
    [InlineData("ls $A $B")]
    [InlineData("ls $A + $B")]
    [InlineData(":ls + 1")]
    [InlineData(":ls = value;")]
    [InlineData(":ls (root)")]
    public void ListingRejectsExtraArgumentsWithoutExecutingCode(string text)
    {
        Assert.True(ListTreeCommand.TryParse(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
    }

    [Theory]
    [InlineData("ls()")]
    [InlineData(":lsx")]
    [InlineData(":ls()")]
    [InlineData("ls.Count")]
    [InlineData("ls + 1")]
    [InlineData("ls = value;")]
    [InlineData("ls += 2; value = ls;")]
    [InlineData("ls (root)")]
    [InlineData("ls ?? root")]
    [InlineData("ls + $Control.GetIndex()")]
    [InlineData("ls is Node")]
    [InlineData("ls / root")]
    [InlineData("ls /rooted")]
    [InlineData("var ls = 1;")]
    [InlineData("\"ls $Control\"")]
    public void OrdinaryCSharpKeepsItsMeaning(string text)
        => Assert.False(ListTreeCommand.TryParse(text, out _));

    [Theory]
    [InlineData("ls", false)]
    [InlineData("ls ", true)]
    [InlineData(":ls", false)]
    [InlineData(" :ls", false)]
    [InlineData(" :ls ", true)]
    public void ListingOnlyAwaitsATargetAfterWhitespace(string text, bool awaiting)
    {
        Assert.True(ListTreeCommand.TryParse(text, out var command));
        Assert.Equal(awaiting, command.AwaitingTarget);
        Assert.Equal(awaiting, command.InArgument(text.Length));
        Assert.False(command.InArgument(command.Start));
    }

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
