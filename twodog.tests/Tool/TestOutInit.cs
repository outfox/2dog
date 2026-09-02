using System.Runtime.CompilerServices;
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
    internal static void PinRedirectedConsole() => Out.PinConsoleFacts(ConsoleFacts.Redirected);
}
