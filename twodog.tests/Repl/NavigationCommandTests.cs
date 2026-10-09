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
    [InlineData("cd.Name")]
    [InlineData("cd = 1;")]
    [InlineData("cd += 2;")]
    [InlineData("cd / 2")]
    [InlineData("cd / root")]
    [InlineData("cd ? foo : bar")]
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
    [InlineData(":cd unknown")]
    [InlineData(":pwd unknown")]
    public void RejectsMalformedCommandsWithoutExecutingCode(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.StartsWith("Usage:", command.Error);
    }

    [Theory]
    [InlineData("cd ")]
    [InlineData(":cd ")]
    public void EmptyArgumentOffersNodeCompletion(string text)
    {
        Assert.True(NavigationCommand.TryParse(text, out var command));
        Assert.True(command.AwaitingTarget);
        Assert.True(command.InArgument(text.Length));
    }
}
