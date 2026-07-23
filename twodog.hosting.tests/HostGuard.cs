namespace twodog.Hosting.Tests;

/// <summary>In-process engine hosting is platform-gated and EngineHost.Start
/// fails closed where unsupported (macOS - deferred to MULTI-INSTANCE-PLAN.md
/// phase 5), so every test that starts instances skips first.</summary>
internal static class HostGuard
{
    public static void SkipUnlessSupported() =>
        Assert.SkipWhen(!EngineHost.IsSupported,
            "In-process engine hosting is deferred on this platform (MULTI-INSTANCE-PLAN.md phase 5).");
}
