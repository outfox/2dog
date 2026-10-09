using PrettyPrompt;
using PrettyPrompt.Highlighting;

namespace twodog.Repl;

internal static class ReplOutput
{
    private static bool Color => !Console.IsOutputRedirected && !PromptConfiguration.HasUserOptedOutFromColor;
    private static string Paint(string text, string style) => Color ? $"\x1b[{style}m{text}\x1b[0m" : text;

    public static void Banner(string scenePath)
    {
        Console.WriteLine();
        Console.WriteLine(Paint("2dog", "1;36") + "  " + Paint("C# REPL", "1;37"));
        Console.WriteLine("Game scene  " + Paint(scenePath, "32"));
        Console.WriteLine();
        InputMode(multiline: false);
        Console.WriteLine("  " + Paint("Tab / Shift+Tab", "36") + "  complete / cycle    " + Paint(":help", "36") + "  examples and controls");
        Console.WriteLine("  " + Paint(":multiline", "36") + "  swap Enter and Shift+Enter");
        Console.WriteLine();
        Console.WriteLine("Explore  " + Paint("ls", "33") + "  or  " + Paint("$Control", "33") + "    Navigate  " + Paint("cd $Control", "33") + "    Find nodes  " + Paint("?Timer", "33"));
        Console.WriteLine();
    }

    public static void Result(string value, FormattedString? styled = null)
    {
        Console.WriteLine(Paint("\u2192 ", "36") + (styled is { } formatted ? NodeColors.Ansi(formatted, Color) : Paint(value, "32")));
        Console.WriteLine();
    }

    public static void Tree(string hierarchy, FormattedString? styled = null)
    {
        Console.WriteLine(styled is { } formatted ? NodeColors.Ansi(formatted.Substring(0, hierarchy.TrimEnd().Length), Color) : hierarchy.TrimEnd());
        Console.WriteLine();
    }

    public static void Error(string error)
    {
        Console.Error.WriteLine(Color && !Console.IsErrorRedirected ? $"\x1b[31m{error}\x1b[0m" : error);
        Console.Error.WriteLine();
    }

    public static void InputMode(bool multiline)
        => Console.WriteLine("  " + Paint("Enter", "36") + (multiline ? "  newline     " : "  run         ") +
            Paint("Shift+Enter", "36") + (multiline ? "  run" : "  newline"));

    public static void Help(bool multiline)
    {
        Console.WriteLine();
        Section("Explore the game");
        Row("scene", "Root node of the game's currently loaded .tscn (tree.CurrentScene).");
        Row("tree / root", "The shared SceneTree / its root Window.");
        Row("here / pwd", "The selected node / its absolute path. Starts at root.");
        Row("engine / ct", "The 2dog host / cancellation token for this submission.");
        Example("ls");
        Example("ls $Control");
        Example("cd $Control/Signals/Table/");
        Example("ls  // children of the selected node");
        Example("cd ..  // parent; cd / returns to root");
        Example("$Control.Size");
        Example("?Timer  // Tab searches node names anywhere in the tree");
        Example("$Control/Signals/Sources/CSharpTicker.Tick();");
        Console.WriteLine("  ls prints a node and all its descendants. Paths are case-sensitive; Tab fills them in.");
        Console.WriteLine("  $ paths start at here and infer the live node's public C# type. cd selects here.");
        Console.WriteLine("  The prompt shows your scope. :cd / :pwd force commands if C# variables have those names.");
        Console.WriteLine("  cd $/root/Control uses an absolute path; ?Name still searches the whole tree.");
        Console.WriteLine("  If the selected node leaves the tree, the scope returns to root.");
        Console.WriteLine("  Use $[\"Control/My Node\"] for spaces or punctuation; C# $\"...\" strings stay strings.");
        Console.WriteLine();
        Section("Change scenes");
        Example("tree.ChangeSceneToFile(\"res://other_scene.tscn\");");
        Example("await tree.ToSignal(tree, SceneTree.SignalName.SceneChanged);");
        Console.WriteLine("  scene follows the new game scene after the change finishes.");
        Console.WriteLine();
        Section("Scratch world beside the game");
        Example("world.Open(\"res://other_scene.tscn\");");
        Example("world.Scene.GetChildren()");
        Example("world.Clear();  // empty scratch scene");
        Example("world.Close();");
        Console.WriteLine("  Independent 2D/3D worlds in a separate window. The SceneTree and singletons are shared.");
        Console.WriteLine();
        Section("Editing and session");
        Row("Enter", "Accept a selected completion; otherwise " + (multiline ? "insert a newline. Shift+Enter runs." : "run the input. Shift+Enter inserts a newline."));
        Row(":multiline", "Swap Enter and Shift+Enter for this session; Ctrl+Enter always runs.");
        Row("Tab / Shift+Tab", "Accept, then cycle matching completions forward / backward. Backspace restores your prefix.");
        Row(". / space / ( / /", "Finish cycling and continue editing. ?Name searches all live node names.");
        Row("Shift+Backspace", "Delete backward to a word break, like Ctrl+Backspace. Dots and slashes are breaks.");
        Row("Ctrl+Space", "Open suggestions. Ctrl+Shift+Space shows method signatures and arguments.");
        Row("Up / Down", "History. Ctrl+C cancels input or cooperatively cancels code; pass ct to waits.");
        Row(":reset / :clear", "Clear C# variables / screen. :quit or Ctrl+D on empty input exits.");
        Console.WriteLine("  #r \"assembly.dll\" and #load \"script.csx\" load local code.");
        Console.WriteLine("  C# and normal await continuations run on Godot's thread; frames advance while awaiting.");
        Console.WriteLine("  Keep Godot calls off Task.Run/ConfigureAwait(false). Synchronous code blocks the game.");
        Console.WriteLine();
    }

    private static void Section(string text) => Console.WriteLine(Paint(text, "1;36"));
    private static void Row(string name, string description) => Console.WriteLine("  " + Paint(name.PadRight(20), "36") + description);
    private static void Example(string text) => Console.WriteLine("  " + Paint(text, "33"));
}
