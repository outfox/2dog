using twodog.Repl;

namespace twodog.tests.ReplTests;

public class NavigationCommandTests
{
    [Theory]
    [InlineData("cd", null)]
    [InlineData("cd;", null)]
    [InlineData("cd /", "/")]
    [InlineData("cd ..", "..")]
    [InlineData("cd .", ".")]
    [InlineData("  cd $Control/Signals/Table/;  ", "Control/Signals/Table")]
    [InlineData("cd $[\"My Node\"]", "My Node")]
    [InlineData(":cd $Child", "Child")]
    [InlineData("cd $/root/Control/", "/root/Control")]
    [InlineData(":cd /root/Control/", "/root/Control")]
    [InlineData("rm", null)]
    [InlineData("rm $Child/", "Child")]
    [InlineData("rm $[\"My Node\"]", "My Node")]
    [InlineData(":rm /root/Control/", "/root/Control")]
    [InlineData("pwd", null)]
    [InlineData(":pwd", null)]
    public void RecognizesNavigationForms(string text, string? path)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.Null(command.Error);
        Assert.Equal(path, command.Path);
        Assert.Equal(text.TrimStart().StartsWith(':'), command.Explicit);
        Assert.True(ReplSession.IsComplete(text));
    }

    [Theory]
    [InlineData("cd()")]
    [InlineData("cd (root)")]
    [InlineData("cd ($Control)")]
    [InlineData("cd += $Control.GetIndex();")]
    [InlineData("cd (root); root.GetChild(0);")]
    [InlineData("cd = 1; pwd = 2;")]
    [InlineData("cd + 2;")]
    [InlineData("cd is Node")]
    [InlineData("cd ?? root")]
    [InlineData("cd.Name")]
    [InlineData("cd = 1;")]
    [InlineData("cd += 2;")]
    [InlineData("cd / 2")]
    [InlineData("cd / root")]
    [InlineData("cd ? foo : bar")]
    [InlineData("rm()")]
    [InlineData("rm (root)")]
    [InlineData("rm = 1;")]
    [InlineData("rm / 2")]
    [InlineData("pwd()")]
    [InlineData("pwd = 1;")]
    [InlineData("pwd + 2")]
    public void LeavesOrdinaryCSharpAlone(string text)
    {
        Assert.False(NavigationCommand.TryParse(text, out _));
        Assert.DoesNotContain(NodePathInput.Find(text), path => path.Search);
    }

    [Theory]
    [InlineData("cd $")]
    [InlineData("cd $Child extra")]
    [InlineData("cd $Child; throw new Exception();")]
    [InlineData(":cd x; throw new Exception();")]
    [InlineData("cd x.GetParent(); throw new Exception();")]
    [InlineData("rm $Child; root.QueueFree();")]
    [InlineData("rm x; root.QueueFree();")]
    [InlineData(":pwd unknown")]
    public void RejectsMalformedCommandsWithoutExecutingCode(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
    }

    [Theory]
    [InlineData("rm x", "x")]
    [InlineData(":rm x.GetChild(0)", "x.GetChild(0)")]
    [InlineData("cd x", "x")]
    [InlineData("cd x;", "x")]
    [InlineData("  :cd x.GetParent();  ", "x.GetParent()")]
    [InlineData("cd scene", "scene")]
    [InlineData("cd nodes[0]", "nodes[0]")]
    [InlineData("cd await FindNodeAsync()", "await FindNodeAsync()")]
    [InlineData("cd $Control.GetParent()", "$Control.GetParent()")]
    [InlineData(":cd (scene ?? root)", "(scene ?? root)")]
    public void AcceptsOneCSharpExpression(string text, string expression)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.Null(command.Error);
        Assert.Null(command.Path);
        Assert.False(command.AwaitingTarget);
        Assert.Equal(expression, command.Expression);
        Assert.Equal(expression, text.Substring(command.ExpressionSpan!.Value.Start, command.ExpressionSpan.Value.Length));
        Assert.True(ReplSession.IsComplete(text));
    }

    [Fact]
    public void UnfinishedExpressionRemainsEditable()
    {
        const string text = "cd x.GetChild(";
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.True(command.InArgument(text.Length));
        Assert.False(ReplSession.IsComplete(text));
    }

    [Theory]
    [InlineData("cd ")]
    [InlineData(":cd ")]
    [InlineData("rm ")]
    [InlineData(":rm ")]
    public void EmptyArgumentOffersNodeCompletion(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.True(command.AwaitingTarget);
        Assert.True(command.InArgument(text.Length));
    }
}
