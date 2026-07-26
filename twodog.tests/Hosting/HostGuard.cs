using System.Runtime.CompilerServices;

namespace twodog.Hosting.Tests;

/// <summary>In-process engine hosting is platform-gated and EngineHost.Start
/// fails closed where unsupported (macOS dual-load is unvalidated), so every
/// test that starts instances skips first.</summary>
internal static class HostGuard
{
    public static void SkipUnlessSupported() =>
        Assert.SkipWhen(!EngineHost.IsSupported,
            "In-process engine hosting is deferred on this platform.");
}

/// <summary>The whole suite shares one process-wide slot counter and mapped
/// slots are never reclaimed, so headroom under the default cap shrinks with
/// every Start-based test added. Raise the cap before any test runs.</summary>
internal static class TestRunSlotBudget
{
    [ModuleInitializer]
    internal static void RaiseInstanceCap() => EngineHost.MaxProcessInstances = 256;
}
