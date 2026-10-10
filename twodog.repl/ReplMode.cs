using System.Diagnostics.CodeAnalysis;

namespace twodog.Repl;

internal enum ReplMode { Auto, Shell, CSharp, Agent }

internal static class ReplModes
{
    internal const string AgentReply = "the lights are on, but there's no one home";
    internal static readonly string[] Controls = [":auto", ":sh", ":cs", ":ai", ":help", ":exit", ":clear", ":reset", ":multiline"];
    internal static readonly string[] ShellCommands = ["ls", "cd", "pwd", "rm", "cp", "mv", "exit"];
    public static string Name(this ReplMode mode) => mode switch
    {
        ReplMode.Shell => "sh", ReplMode.CSharp => "cs", ReplMode.Agent => "ai", _ => "auto",
    };
    public static bool HasShell(this ReplMode mode) => mode is ReplMode.Auto or ReplMode.Shell;
    public static bool TryParse(string text, out ReplMode mode)
    {
        mode = text switch { ":sh" => ReplMode.Shell, ":cs" => ReplMode.CSharp, ":ai" => ReplMode.Agent, _ => ReplMode.Auto };
        return text is ":auto" or ":sh" or ":cs" or ":ai";
    }
    public static bool TryNavigation(this ReplMode mode, string text, [NotNullWhen(true)] out NavigationCommand? command)
    {
        command = null;
        return mode.HasShell() && NavigationCommand.TryParse(text, out command, forceShell: mode == ReplMode.Shell);
    }
    public static bool TryListing(this ReplMode mode, string text, [NotNullWhen(true)] out ListTreeCommand? command)
    {
        command = null;
        return mode.HasShell() && ListTreeCommand.TryParse(text, out command, forceShell: mode == ReplMode.Shell);
    }
}
