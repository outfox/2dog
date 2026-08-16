using Spectre.Console;

namespace twodog.cli;

/// <summary>
/// The interactive half of the tool: it only ever gathers the same values the
/// flags carry, so every prompt here has a command-line equivalent and can be
/// skipped entirely.
/// </summary>
internal static class Tui
{
    /// <summary>
    /// Whether prompting is possible at all. Redirected input (pipes, CI) or a
    /// non-interactive terminal falls back to flags and defaults.
    /// </summary>
    public static bool CanPrompt =>
        !Console.IsInputRedirected && AnsiConsole.Profile.Capabilities.Interactive;

    public static void Header() => Out.Header();

    public static void ShowProject(ProjectContext project)
    {
        var what = project.IsNew ? "new project" : "project";
        Out.Line($"[grey]{what}[/]  [bold]{Markup.Escape(project.BaseName)}[/] " +
                 $"[grey]({Markup.Escape(project.Dir)})[/]");

        if (project.ExistingHosts.Count > 0)
        {
            var hosts = project.ExistingHosts
                .Select(h => $"{Markup.Escape(h.Folder)} [grey]({Hosts.Label(h.Kind)})[/]");
            Out.Line($"[grey]hosts[/]    {string.Join("[grey],[/] ", hosts)}");
        }

        Out.Blank();
    }

    /// <summary>The project name for a new project. No silent rewriting: an
    /// answer sanitization would alter is rejected, like the folder prompts.</summary>
    public static string AskProjectName(string? suggestion)
    {
        var prompt = new TextPrompt<string>("Project name:")
            .Validate(value => Hosts.SanitizeName(value) is { } name && name == value.Trim()
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]only letters, digits, '.', '_' and '-'[/]"));
        if (Hosts.SanitizeName(suggestion) is { } valid) prompt.DefaultValue(valid);
        return AnsiConsole.Prompt(prompt).Trim();
    }

    /// <summary>
    /// The interactive way out of the spaced-name refusal: explain, confirm,
    /// and gather the new name. Null when the user declines (the caller then
    /// surfaces the manual checklist).
    /// </summary>
    public static string? OfferRename(SpacedNameException problem)
    {
        Out.Line($"[yellow]![/] The project's .NET name [bold]{Markup.Escape(problem.OldName)}[/] contains spaces.");
        Out.Line("[grey]  .NET publish silently drops such a project's NuGet packages from hosts that[/]");
        Out.Line("[grey]  reference it (dotnet/sdk bug), so the name must change before hosts are added.[/]");
        Out.Blank();
        if (!AnsiConsole.Confirm("Rename the project's .NET identity? (the Godot display name keeps its spaces)"))
            return null;

        var prompt = new TextPrompt<string>("New name:")
            .Validate(value => Hosts.SanitizeName(value) is { } name && name == value.Trim()
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]only letters, digits, '.', '_' and '-'[/]"));
        if (problem.Suggested is { } suggested) prompt.DefaultValue(suggested);
        var answer = AnsiConsole.Prompt(prompt).Trim();
        AnsiConsole.WriteLine();
        return answer;
    }

    /// <summary>Where a new project is created (relative paths are fine).</summary>
    public static string AskDirectory(string suggestion) =>
        AnsiConsole.Prompt(new TextPrompt<string>("Directory:")
            .DefaultValue(suggestion)
            .Validate(value => string.IsNullOrWhiteSpace(value)
                ? ValidationResult.Error("[red]a directory is required[/]")
                : ValidationResult.Success()));

    /// <summary>
    /// The checkbox list of hosts. Kinds the project already has start
    /// unchecked and are pre-named so that checking one adds a second host of
    /// that kind rather than colliding with the first.
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

        var prompt = new MultiSelectionPrompt<HostChoice>()
            .Title("Which [green]hosts[/] do you want?")
            .NotRequired()
            .InstructionsText("[grey](space toggles, enter accepts, nothing selected is fine)[/]")
            .UseConverter(Describe);
        foreach (var choice in choices)
        {
            var item = prompt.AddChoice(choice);
            if (choice.Selected) item.Select();
        }

        var selected = AnsiConsole.Prompt(prompt);
        AnsiConsole.WriteLine();
        if (selected.Count == 0) return [];

        if (AnsiConsole.Confirm("Change the folder names?", false))
            RenameHosts(selected, project);

        return selected.Select(c => new HostSpec(c.Kind, c.Folder)).ToList();
    }

    private static void RenameHosts(IReadOnlyList<HostChoice> selected, ProjectContext project)
    {
        var taken = new List<string>(project.TakenFolders);
        foreach (var choice in selected)
        {
            var others = taken.Concat(selected.Where(c => c != choice).Select(c => c.Folder)).ToList();
            choice.Folder = AnsiConsole.Prompt(new TextPrompt<string>($"  {Hosts.Label(choice.Kind)} folder:")
                .DefaultValue(choice.Folder)
                .Validate(value => Validate(value, others)));
        }

        AnsiConsole.WriteLine();
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
        var suffix = choice.KindPresent ? " [yellow](adds a second one)[/]" : "";
        return $"[bold]{Hosts.Label(choice.Kind),-8}[/] {Markup.Escape(choice.Folder)}  " +
               $"[grey]{Hosts.Blurb(choice.Kind)}[/]{suffix}";
    }

    /// <summary>Shows the plan and asks whether to apply it.</summary>
    public static bool ConfirmPlan(IReadOnlyList<string> descriptions)
    {
        Out.Plan(descriptions);
        return AnsiConsole.Confirm($"Apply {descriptions.Count} change(s)?");
    }

    private sealed class HostChoice(HostKind kind, string folder, bool selected, bool kindPresent)
    {
        public HostKind Kind { get; } = kind;
        public string Folder { get; set; } = folder;
        public bool Selected { get; } = selected;

        /// <summary>The project already has a host of this kind.</summary>
        public bool KindPresent { get; } = kindPresent;
    }
}
