using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace twodog.tests.ReplTests;

internal static class ReplDiscoveryInit
{
    /// <summary>Hosts started by tests publish here, never next to the user's real REPL hosts.</summary>
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), "2dog-tests", "repl-" + Environment.ProcessId);

    /// <summary>Unique, short pipe names: macOS limits the socket path under $TMPDIR to 104 characters.</summary>
    internal static string PipeName(string prefix = "2dog-test") => prefix + "-" + Guid.NewGuid().ToString("N")[..12];

    [ModuleInitializer]
    internal static void UsePrivateDiscoveryDirectory()
    {
        // Copies of this assembly in other load contexts must not touch the default context's statics.
        if (AssemblyLoadContext.GetLoadContext(typeof(ReplDiscoveryInit).Assembly) != AssemblyLoadContext.Default) return;
        twodog.Repl.ReplDiscovery.Root = Root;
        twodog.cli.Repl.ReplDiscovery.Root = Root;
    }
}
