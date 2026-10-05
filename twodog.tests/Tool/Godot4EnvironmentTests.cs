using twodog.cli;

namespace twodog.tests.ToolTests;

public class Godot4EnvironmentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsetOrEmptyVariable_WarnsWithManualRemedy(string? value)
    {
        var env = new FakeEnvironment();
        if (value != null) env.Vars["GODOT4"] = value;
        var finding = EnvironmentChecks.Godot4Finding(env);
        Assert.Equal("env.godot4", finding.Id);
        Assert.Equal(Severity.Warn, finding.Severity);
        Assert.Contains("not set", finding.Title);
        Assert.Contains("restart your IDE", finding.Remedy);
        Assert.Null(finding.Fix);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingFileOrDirectory_Warns(bool directory)
    {
        using var tmp = new TempProjectDir();
        var env = new FakeEnvironment();
        env.Vars["GODOT4"] = directory ? tmp.Dir : Path.Combine(tmp.Dir, "missing.exe");
        Assert.Equal(Severity.Warn, EnvironmentChecks.Godot4Finding(env).Severity);
    }

    [Fact]
    public void ExistingExecutable_Passes()
    {
        var env = new FakeEnvironment();
        env.Vars["GODOT4"] = Environment.ProcessPath!;
        Assert.Equal(Severity.Pass, EnvironmentChecks.Godot4Finding(env).Severity);
    }
}
