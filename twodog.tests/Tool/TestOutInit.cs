using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using twodog.cli;

namespace twodog.tests.ToolTests;

internal static class TestOutInit
{
    /// <summary>
    /// The CLI's Spectre layer must render plain, unwrapped text in this
    /// process: tests capture Console.Out into StringWriters, and a test
    /// host's attached terminal (ANSI support, narrow width) would otherwise
    /// leak into the captured strings. Pinning "everything redirected" also
    /// rules prompts and spinners out.
    /// </summary>
    [ModuleInitializer]
    internal static void PinRedirectedConsole()
    {
        // Tests load copies of this assembly into other load contexts (the sanitizer tests, the engine fixtures),
        // and each copy runs this initializer again. A copy in a plain context resolves twodog from the default
        // context, so pinning there would reset the shared Out mid-run, outside CliConsole's lock.
        if (AssemblyLoadContext.GetLoadContext(typeof(TestOutInit).Assembly) != AssemblyLoadContext.Default) return;
        Out.PinConsoleFacts(ConsoleFacts.Redirected);
    }
}
