using System.Text.RegularExpressions;

namespace twodog.Repl;

internal sealed class ReplOutput(TextWriter output, TextWriter error, bool color, bool errorColor)
{
    /// <summary>False when NO_COLOR is set to any value, or TERM=dumb.</summary>
    internal static bool ColorEnabled => Environment.GetEnvironmentVariable("NO_COLOR") is null &&
        Environment.GetEnvironmentVariable("TERM") != "dumb";

    internal static ReplOutput ForConsole() => new(Console.Out, Console.Error,
        !Console.IsOutputRedirected && ColorEnabled, !Console.IsErrorRedirected && ColorEnabled);

    /// <summary>Colors Godot's plain log lines from captured native output: errors red, warnings yellow, locations dim.</summary>
    internal static string StyleLog(string text) => LogLinePattern.Replace(text, match => match.Groups["kind"].Value switch
    {
        "WARNING" or "USER WARNING" => "\x1b[33m" + match.Value + "\x1b[0m",
        "" => "\x1b[2m" + match.Value + "\x1b[0m",
        _ => "\x1b[31m" + match.Value + "\x1b[0m",
    });

    private static readonly Regex LogLinePattern = new(@"^(?:(?<kind>ERROR|WARNING|SCRIPT ERROR|SHADER ERROR|USER ERROR|USER WARNING):[^\r\n]*|[ \t]+at: [^\r\n]*)", RegexOptions.Multiline);

    public void Message(string text) => output.WriteLine(text);
    internal void Write(string? text, bool isError = false) => (isError ? error : output).Write(text);
    private string Paint(string text, string style) => color ? $"\x1b[{style}m{text}\x1b[0m" : text;

    public void Banner(string scenePath)
    {
        output.WriteLine();
        output.WriteLine(Paint("2dog", "1;36") + "  " + Paint("C# REPL", "1;37"));
        output.WriteLine("Game scene  " + Paint(scenePath, "32"));
        output.WriteLine();
        InputMode(multiline: false);
        output.WriteLine("  " + Paint("Tab / Shift+Tab", "36") + "  complete / cycle    " + Paint(":help", "36") + "  examples and controls");
        output.WriteLine("  " + Paint(":auto / :sh / :cs / :ai", "36") + "  switch modes (default: auto)");
        output.WriteLine("  " + Paint(":multiline", "36") + "  swap Enter and Shift+Enter");
        output.WriteLine();
        output.WriteLine("Explore  " + Paint("ls", "33") + "  or  " + Paint("$Control", "33") + "    Navigate  " + Paint("cd $Control", "33") + "    Find nodes  " + Paint("$Timer", "33") + " Tab");
        output.WriteLine();
    }

    public void Result(string value, StyledText? styled = null)
    {
        output.WriteLine(Paint("\u2192 ", "36") + (styled is { } formatted ? formatted.ToAnsi(color) : Paint(value, "32")));
        output.WriteLine();
    }

    public void Tree(string hierarchy, StyledText? styled = null)
    {
        output.WriteLine(styled is { } formatted ? formatted.Substring(0, hierarchy.TrimEnd().Length).ToAnsi(color) : hierarchy.TrimEnd());
        output.WriteLine();
    }

    public void Error(string message)
    {
        error.WriteLine(errorColor ? $"\x1b[31m{message}\x1b[0m" : message);
        error.WriteLine();
    }

    public void InputMode(bool multiline)
        => output.WriteLine("  " + Paint("Enter", "36") + (multiline ? "  newline     " : "  run         ") +
            Paint("Shift+Enter", "36") + (multiline ? "  run" : "  newline"));

    /// <summary>The working guide for coding agents, shared with '2dog skill'.</summary>
    internal static string AgentGuide { get; } = ReadGuide();

    private static string ReadGuide()
    {
        using var stream = typeof(ReplOutput).Assembly.GetManifestResourceStream("twodog.repl.repl-guide.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
    }

    public void Guide() => output.WriteLine(AgentGuide + Environment.NewLine);

    public void Help(bool multiline, ReplMode mode = ReplMode.Auto)
    {
        output.WriteLine();
        Section("Modes (current: :" + mode.Name() + ")");
        Row(":auto", "Default: C# and shell commands together.");
        Row(":sh", "Shell commands take priority. Inspect properties and call node methods with C# expressions.");
        Row(":cs", "Pure C#: no shell commands; typed $ paths and completion still work.");
        Row(":ai", "Agent placeholder; input only gets a reply and never executes.");
        output.WriteLine("  Modes share C# variables and the selected node. :reset keeps the current mode.");
        output.WriteLine();
        Section("Explore the game");
        Row("scene", "Root node of the game's currently loaded .tscn (tree.CurrentScene).");
        Row("tree / root", "The shared SceneTree / its root Window.");
        Row("here / pwd", "The selected node / its absolute path. Starts at root.");
        Row("engine / ct", "The 2dog host / cancellation token for this submission.");
        Example("ls");
        Example("ls $Control");
        Example("rm $Child  // QueueFree this node and its descendants; a target is required");
        Example("cp $Source $Parent  // duplicate the subtree into an existing parent");
        Example("mv savedNode destination  // reparent, preserving the global transform");
        Example("mv $Old \"NewName\"  // rename without changing parent or node identity");
        Example("cd $Control/Signals/Table/");
        Example("var x = here;  // save a node, then use cd x or cd x.GetParent()");
        Example("ls  // children of the selected node");
        Example("cd ..  // parent; cd / returns to root");
        Example("$Control.Size");
        Example("$Timer  // Tab: matching paths first, then node names anywhere in the tree");
        Example("$Control/Signals/Sources/CSharpTicker.Tick();");
        output.WriteLine("  ls prints a node and all its descendants; ls -d 1 stops one level down. Paths are case-sensitive; Tab fills them in.");
        output.WriteLine("  $ paths start at here and infer the live node's public C# type. cd selects here.");
        output.WriteLine("  The prompt shows your scope. :cd / :pwd force commands if C# variables have those names.");
        output.WriteLine("  cd /root/Control and $/root/Control use absolute paths. Tab also searches node names anywhere in the tree.");
        output.WriteLine("  cp/mv take source and existing parent expressions; mv also takes a string new name. Name collisions are rejected.");
        output.WriteLine("  mv(...), mv.Member, mv[index] stay C#. :mv forces the command; @mv selects the identifier.");
        output.WriteLine("  If the selected node leaves the tree, the scope returns to root.");
        output.WriteLine("  Use $[\"Control/My Node\"] for spaces or punctuation; C# $\"...\" strings stay strings.");
        output.WriteLine();
        Section("Change scenes");
        Example("tree.ChangeSceneToFile(\"res://other_scene.tscn\");");
        Example("await tree.ToSignal(tree, SceneTree.SignalName.SceneChanged);");
        output.WriteLine("  scene follows the new game scene after the change finishes.");
        output.WriteLine();
        Section("Scratch worlds beside the game");
        Example("world.Open(\"res://other_scene.tscn\");");
        Example("world.Scene.GetChildren()");
        Example("world.Clear();  // empty scratch scene");
        Example("world.Close();");
        Example("worlds.Add(\"lab\", \"res://other_scene.tscn\");");
        Example("worlds[\"lab\"].Scene.GetChildren()");
        Example("worlds.Names  // names belong to this session");
        Example("worlds.Remove(\"lab\");");
        output.WriteLine("  Independent 2D/3D worlds in a separate window. The SceneTree and singletons are shared.");
        output.WriteLine("  Tab completes eligible res:// paths in resource strings; scene calls offer scenes.");
        output.WriteLine();
        Section("Editing and session");
        Row("Enter", "Accept a selected completion; otherwise " + (multiline ? "insert a newline. Shift+Enter runs." : "run the input. Shift+Enter inserts a newline."));
        output.WriteLine("  Enter runs exact bare commands with no trailing space; shell commands apply in :auto and :sh.");
        Row(":multiline", "Swap Enter and Shift+Enter for this session; Ctrl+Enter always runs.");
        Row(":help agents", "The guide for coding agents working through '2dog repl exec'.");
        Row("Tab / Shift+Tab", "Accept, then cycle matching completions forward / backward. Backspace restores your prefix.");
        Row(". / (", "Accept a selection outside quoted path text and finish cycling.");
        Row("/", "Accept only for a nonempty relative path outside quotes; absolute-path slashes stay literal.");
        Row("Space", "Insert literally and finish any completion cycle.");
        Row("$Name", "Complete matching paths first, then search node names anywhere in the tree.");
        Row("Shift+Backspace", "Delete backward to a word break, like Ctrl+Backspace. Dots and slashes are breaks.");
        Row("Ctrl+Space", "Open suggestions. Ctrl+Shift+Space shows method signatures and arguments.");
        Row("Up / Down", "Move between lines, then browse history; typed text filters it.");
        Row("Insert", "Toggle overwrite: typing replaces the character at the caret. Backspace / Delete delete left / right.");
        Row("Ctrl+C", "Clear the input and cancel running/queued work; pass ct to waits. Copies a selection instead.");
        Row(":cancel", "Cancel running and queued work. Submitted C# loops have cancellation checkpoints.");
        Row("Ctrl+Z / Ctrl+Y", "Undo / redo. Ctrl+A selects all; Ctrl+X / Ctrl+V cut and paste; Ctrl+K / Ctrl+U delete to line end / start.");
        Row(":reset / :clear", "Clear C# variables / screen (also Ctrl+L). :exit or Ctrl+D on empty input exits.");
        output.WriteLine("  Output scrolls above the input, which stays editable while submissions run; queue the next one any time.");
        output.WriteLine("  Pasted code keeps its line breaks and waits for Enter.");
        output.WriteLine("  #r \"assembly.dll\" and #load \"script.csx\" load local code.");
        output.WriteLine("  C# and normal await continuations run on Godot's thread; frames advance while awaiting.");
        output.WriteLine("  Keep Godot calls off Task.Run/ConfigureAwait(false). Synchronous code blocks the game.");
        output.WriteLine("  Other terminals and agents: 2dog repl attaches; 2dog repl exec -s NAME CODE runs code in a named session.");
        output.WriteLine();
    }

    private void Section(string text) => output.WriteLine(Paint(text, "1;36"));
    private void Row(string name, string description) => output.WriteLine("  " + Paint(name.PadRight(20), "36") + description);
    private void Example(string text) => output.WriteLine("  " + Paint(text, "33"));
}
