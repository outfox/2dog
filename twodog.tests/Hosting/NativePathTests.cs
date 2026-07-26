using twodog.Hosting.Xunit;
using Engine = twodog.Engine;

namespace twodog.Hosting.Tests;

/// <summary>Boots on its pooled copy, restarts with a DIFFERENT native path in
/// the same load context, and reports whether the switch was rejected.</summary>
public sealed class NativeSwitchProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx)
    {
        var otherNative = (string)(ctx.State ?? throw new InvalidOperationException("needs a native path as State"));
        var engine = new Engine(ctx.Tag, ctx.ProjectDir, ctx.Args) { NativePath = ctx.NativePath };
        using (engine.Start())
        {
        }
        engine.Dispose();

        var sneaky = new Engine(ctx.Tag, ctx.ProjectDir, ctx.Args) { NativePath = otherNative };
        try
        {
            sneaky.Start();
            sneaky.Dispose();
            return 1; // a context must never switch native libraries
        }
        catch (InvalidOperationException e) when (e.Message.Contains("cannot switch"))
        {
            return 0;
        }
    }
}

public sealed class NativePathTests
{
    [Fact]
    public async Task SwitchingNativePathWithinOneContextIsRejected()
    {
        HostGuard.SkipUnlessSupported();
        var dir = ScratchProject.Create("native-switch");
        // Rejection happens before any load, so any existing file with a
        // different path exercises it.
        var other = Path.Combine(Path.GetTempPath(), $"2dog-fake-native-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(other, [1, 2, 3]);
        try
        {
            using var host = new EngineHost();
            var instance = host.Start<NativeSwitchProgram>(new()
            { Tag = "native-switch", ProjectDir = dir, Args = ["--headless"], State = other });
            Assert.Equal(0, await instance.Completion.WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(other);
            ScratchProject.Delete(dir);
        }
    }
}
