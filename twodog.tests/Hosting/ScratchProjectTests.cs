using twodog.Hosting.Xunit;

namespace twodog.tests.HostingTests;

// Serialized: the test parks the process-wide CWD in a deleted directory, which engines and subprocesses of
// parallel collections would inherit.
[CollectionDefinition(nameof(ScratchProjectCwdCollection), DisableParallelization = true)]
public sealed class ScratchProjectCwdCollection;

[Collection(nameof(ScratchProjectCwdCollection))]
public sealed class ScratchProjectTests
{
    [Fact]
    public void Delete_LeavesALiveCwd_WhenTheCwdWasAlreadyDeleted()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows cannot delete a process's current directory");
        var dead = ScratchProject.Create("cwd-dead");
        var target = ScratchProject.Create("cwd-target");
        try
        {
            // The state a concurrent engine's DirAccess leaves behind when it restores a CWD that is then deleted.
            Environment.CurrentDirectory = dead;
            Directory.Delete(dead, recursive: true);

            ScratchProject.Delete(target);

            Assert.False(Directory.Exists(target), "A deleted CWD must not block scratch cleanup.");
            Assert.True(Directory.Exists(Environment.CurrentDirectory));
        }
        finally
        {
            Environment.CurrentDirectory = AppContext.BaseDirectory;
            ScratchProject.Delete(target);
        }
    }
}
