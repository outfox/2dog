using twodog.cli;

namespace twodog.tests.ToolTests;

/// <summary>
/// Runs the in-process 2dog CLI with Console.Out/Error captured, so expected
/// usage and error output never leaks into the test log and can be asserted
/// on instead. The console is process-global state; a lock keeps the swap
/// safe while test collections run in parallel.
/// </summary>
internal static class CliConsole
{
    private static readonly Lock Gate = new();

    public static (int ExitCode, string Stdout, string Stderr) Run(params string[] args) =>
        Capture(() => Program.Main(args));

    public static (int ExitCode, string Stdout, string Stderr) Capture(Func<int> action)
    {
        lock (Gate)
        {
            var (originalOut, originalError) = (Console.Out, Console.Error);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                return (action(), stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }
}
