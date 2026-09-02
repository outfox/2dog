using Spectre.Console;

namespace twodog.cli;

/// <summary>
/// The interactive half of the tool: it only gathers the same values the flags carry, so every prompt has a
/// command-line equivalent and can be skipped entirely. Prompts render on the stdout console and honour Ctrl+C.
/// </summary>
internal static class Tui
{
    /// <summary>
    /// Whether prompting is possible at all; redirected input, CI or a non-interactive terminal falls back to flags.
    /// </summary>
    public static bool CanPrompt => Out.Mode.CanPrompt && Out.Console.Profile.Capabilities.Interactive;

    /// <summary>Runs a prompt on the stdout console; Ctrl+C cancels it instead of killing the process mid-draw.</summary>
    private static T Ask<T>(IPrompt<T> prompt) =>
        Out.Console.PromptAsync(prompt, Cancellation.Token).GetAwaiter().GetResult();

    private static bool Confirm(string text, bool defaultValue = true) =>
        Out.Console.ConfirmAsync(text, defaultValue, Cancellation.Token).GetAwaiter().GetResult();

    public static void Header() => Out.Header();

    public static void ShowProject(ProjectContext project) =>
        Out.ProjectSummary(project.IsNew ? "new project" : "project", project.BaseName, project.Dir,
            project.ExistingHosts.Select(h => (h.Folder, h.Kind)));

    /// <summary>The project name for a new project. No silent rewriting: an
    /// answer sanitization would alter is rejected, like the folder prompts.</summary>
    public static string AskProjectName(string? suggestion)
    {
        var prompt = new TextPrompt<string>("Project name:")
            .Validate(value => Hosts.SanitizeName(value) is { } name && name == value.Trim()
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]only letters, digits, '.', '_' and '-'[/]"));
        if (Hosts.SanitizeName(suggestion) is { } valid) prompt.DefaultValue(valid);
        return Ask(prompt).Trim();
    }

    /// <summary>
    /// The interactive way out of the spaced-name refusal: explain, confirm, gather the new name. Null when the
    /// user declines (the caller then surfaces the manual checklist).
    /// </summary>
    public static string? OfferRename(SpacedNameException problem)
    {
        Out.Info($"[yellow]![/] The project's .NET name [bold]{Markup.Escape(problem.OldName)}[/] contains spaces.");
        Out.Info("[grey]  .NET publish silently drops such a project's NuGet packages from hosts that[/]");
        Out.Info("[grey]  reference it (dotnet/sdk bug), so the name must change before hosts are added.[/]");
        Out.Blank();
        if (!Confirm("Rename the project's .NET identity? (the Godot display name keeps its spaces)"))
            return null;

        var prompt = new TextPrompt<string>("New name:")
            .Validate(value => Hosts.SanitizeName(value) is { } name && name == value.Trim()
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]only letters, digits, '.', '_' and '-'[/]"));
        if (problem.Suggested is { } suggested) prompt.DefaultValue(suggested);
        var answer = Ask(prompt).Trim();
        Out.Blank();
        return answer;
    }

    /// <summary>Where a new project is created (relative paths are fine).</summary>
    public static string AskDirectory(string suggestion) =>
        Ask(new TextPrompt<string>("Directory:")
            .DefaultValue(suggestion)
            .Validate(value => string.IsNullOrWhiteSpace(value)
                ? ValidationResult.Error("[red]a directory is required[/]")
                : ValidationResult.Success()));

    /// <summary>Creating into a directory that already has files is fine, but worth a question.</summary>
    public static bool ConfirmNonEmptyDirectory(string dir) =>
        Confirm($"{Markup.Escape(dir)} is not empty - create the project alongside its files? (existing files are kept)",
            false);

    /// <summary>
    /// The host picker. Kinds the project already has start unchecked and pre-named so checking one adds a second
    /// host of that kind rather than colliding with the first. Accessible mode asks one yes/no question per kind.
    /// </summary>
    public static List<HostSpec> SelectHosts(ProjectContext project, IReadOnlyList<HostSpec> preselected)
    {
        var taken = new List<string>(project.TakenFolders);
        var choices = new List<HostChoice>();
        foreach (var kind in Hosts.All)
        {
            var chosen = preselected.FirstOrDefault(h => h.Kind == kind);
            var folder = chosen?.Folder ?? Hosts.AllocateFolder(kind, project.BaseName, taken);
            taken.Add(folder);
            choices.Add(new HostChoice(kind, folder, chosen != null,
                project.ExistingHosts.Any(h => h.Kind == kind)));
        }

        var selected = Out.Mode.Accessible ? SelectSequentially(choices) : SelectFromList(choices);
        if (selected.Count == 0) return [];

        if (Confirm("Change the folder names?", false))
            RenameHosts(selected, project);

        return selected.Select(c => new HostSpec(c.Kind, c.Folder)).ToList();
    }

    private static List<HostChoice> SelectFromList(List<HostChoice> choices)
    {
        var prompt = new MultiSelectionPrompt<HostChoice>()
            .Title("Which [green]hosts[/] do you want?")
            .NotRequired()
            .PageSize(12)
            .Mode(SelectionMode.Leaf)
            .InstructionsText("[grey](space toggles, enter accepts, nothing selected is fine)[/]")
            .UseConverter(Describe);
        foreach (var group in new[] { HostGroup.Default, HostGroup.OptIn, HostGroup.WindowsOnly })
        {
            var members = choices.Where(c => Hosts.Group(c.Kind) == group).ToList();
            if (members.Count > 0) prompt.AddChoiceGroup(HostChoice.GroupRow(group), members);
        }

        foreach (var choice in choices.Where(c => c.Selected)) prompt.Select(choice);

        var selected = Ask(prompt).Where(c => !c.IsGroup).ToList();
        Out.Blank();
        return selected;
    }

    private static List<HostChoice> SelectSequentially(List<HostChoice> choices)
    {
        Out.Info("Which hosts do you want? One yes/no question per kind.");
        var selected = new List<HostChoice>();
        var n = 0;
        foreach (var choice in choices)
        {
            var second = choice.KindPresent ? ", a second one" : "";
            var question = $"{++n}. {Hosts.Label(choice.Kind)} ({Hosts.Blurb(choice.Kind)}) as {choice.Folder}{second}?";
            if (Confirm(Markup.Escape(question), choice.Selected)) selected.Add(choice);
        }

        Out.Blank();
        return selected;
    }

    private static void RenameHosts(IReadOnlyList<HostChoice> selected, ProjectContext project)
    {
        var taken = new List<string>(project.TakenFolders);
        foreach (var choice in selected)
        {
            var others = taken.Concat(selected.Where(c => c != choice).Select(c => c.Folder)).ToList();
            choice.Folder = Ask(new TextPrompt<string>($"  {Hosts.Label(choice.Kind)} folder:")
                .DefaultValue(choice.Folder)
                .Validate(value => Validate(value, others)));
        }

        Out.Blank();
    }

    private static ValidationResult Validate(string value, List<string> taken)
    {
        var name = Hosts.SanitizeName(value);
        if (name is null) return ValidationResult.Error("[red]needs at least one letter or digit[/]");
        if (name != value.Trim()) return ValidationResult.Error("[red]only letters, digits, '.', '_' and '-'[/]");
        return taken.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? ValidationResult.Error("[red]that folder is already taken[/]")
            : ValidationResult.Success();
    }

    private static string Describe(HostChoice choice)
    {
        if (choice.IsGroup) return $"[grey]{choice.Folder}[/]";
        var suffix = choice.KindPresent ? " [yellow](adds a second one)[/]" : "";
        return $"[bold]{Hosts.Label(choice.Kind),-8}[/] {Markup.Escape(choice.Folder)}  " +
               $"[grey]{Hosts.Blurb(choice.Kind)}[/]{suffix}";
    }

    /// <summary>
    /// doctor's checklist of fixes: safe ones start checked, announced ones unchecked and tagged. Accessible mode
    /// asks one yes/no question per fix.
    /// </summary>
    public static List<Fix> SelectFixes(IReadOnlyList<Fix> fixes)
    {
        if (fixes.Count == 0) return [];
        Out.Blank();
        if (Out.Mode.Accessible)
        {
            var chosen = new List<Fix>();
            var n = 0;
            foreach (var fix in fixes)
                if (Confirm(Markup.Escape($"{++n}. {fix.Description} ({Tag(fix)})?"), fix.Class == FixClass.Safe)) chosen.Add(fix);
            Out.Blank();
            return chosen;
        }

        var prompt = new MultiSelectionPrompt<Fix>()
            .Title("Apply fixes?")
            .NotRequired()
            .PageSize(15)
            .InstructionsText("[grey](space toggles, enter accepts; announced fixes rewrite or replace files)[/]")
            .UseConverter(fix => $"{Markup.Escape(fix.Description)}  [grey]({Tag(fix)})[/]");
        prompt.AddChoices(fixes);
        foreach (var fix in fixes.Where(f => f.Class == FixClass.Safe)) prompt.Select(fix);
        var selected = Ask(prompt);
        Out.Blank();
        return selected;

        static string Tag(Fix fix) => fix.Class == FixClass.Safe ? "safe" : "announced";
    }

    /// <summary>Shows the plan and asks whether to apply it.</summary>
    public static bool ConfirmPlan(IReadOnlyList<ActionReport> plan)
    {
        Out.Plan(plan);
        return Confirm($"Apply {plan.Count} change(s)?");
    }

    private sealed class HostChoice(HostKind kind, string folder, bool selected, bool kindPresent)
    {
        public HostKind Kind { get; } = kind;
        public string Folder { get; set; } = folder;
        public bool Selected { get; } = selected;

        /// <summary>The project already has a host of this kind.</summary>
        public bool KindPresent { get; } = kindPresent;

        /// <summary>A group header row in the picker, never a host.</summary>
        public bool IsGroup { get; private init; }

        public static HostChoice GroupRow(HostGroup group) => new(HostKind.Desktop, group switch
        {
            HostGroup.Default => "default set",
            HostGroup.OptIn => "opt-in",
            _ => "Windows-only",
        }, false, false) { IsGroup = true };
    }
}
