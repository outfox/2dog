namespace twodog.tests.ToolTests;

// The showcase's FPS counter is a symlink to the template's, which ships to users. A checkout without
// core.symlinks writes the link as a text file, and the Godot editor's Windows save replaces it with a copy.
[Trait("Precision", "Independent")]
public class FpsCounterDriftTests
{
    [Fact]
    public void ShowcaseFpsCounter_LinksToTheTemplateScene()
    {
        var showcase = Path.Combine(HelperToolTestBed.RepoRoot, "demos", "showcase", "fps_counter.tscn");
        var template = Path.Combine(HelperToolTestBed.RepoRoot, "templates", "twodog", "fps_counter.tscn");

        var target = File.ResolveLinkTarget(showcase, returnFinalTarget: true);
        Assert.True(target != null, $"{showcase} is not a symlink: enable core.symlinks, restore it with git checkout, " +
                                    "and edit templates/twodog/fps_counter.tscn instead");
        Assert.Equal(Path.GetFullPath(template), target.FullName, ignoreCase: OperatingSystem.IsWindows());
    }
}
