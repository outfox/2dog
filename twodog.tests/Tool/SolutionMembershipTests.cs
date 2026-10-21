using twodog.cli;

namespace twodog.tests.ToolTests;

public class SolutionMembershipTests
{
    [Theory]
    [InlineData("<Solution><Project Path=\"tools/OtherGame.csproj\" /></Solution>", false)]
    [InlineData("<Solution><Project Path=\"Game.csproj.old\" /></Solution>", false)]
    [InlineData("<Solution><Project Path=\"Game.csproj\" /></Solution>", true)]
    [InlineData("Project(\"{type}\") = \"OtherGame\", \"tools\\OtherGame.csproj\", \"{id}\"", false)]
    [InlineData("Project(\"{type}\") = \"Game\", \"Game.csproj\", \"{id}\"", true)]
    public void ContainsProjectMatchesTheWholeFileName(string contents, bool expected)
    {
        using var tmp = new TempProjectDir();
        var solution = tmp.Write("Game.slnx", contents);
        Assert.Equal(expected, SolutionOps.ContainsProject(solution, "Game.csproj"));
    }

    [Fact]
    public void LocateDoesNotConfuseAnotherProjectWithTheGame()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("Other.slnx", "<Solution><Project Path=\"OtherGame.csproj\" /></Solution>");
        var solution = tmp.Write("Game.slnx", "<Solution><Project Path=\"Game.csproj\" /></Solution>");
        Assert.Equal((solution, true), SolutionOps.Locate(tmp.Dir, "Game"));
    }
}
