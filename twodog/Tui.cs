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

    public static void Header()
    {
        AnsiConsole.MarkupLine($"[bold]2dog[/] [grey]{ToolVersions.TwoDogVersion}[/]  [grey]https://2dog.dev[/]");
        AnsiConsole.WriteLine();
    }

    public static void ShowProject(ProjectContext project)
    {
        var what = project.IsNew ? "new project" : "project";
        AnsiConsole.MarkupLine($"[grey]{what}[/]  [bold]{Markup.Escape(project.BaseName)}[/] " +
                               $"[grey]({Markup.Escape(project.Dir)})[/]");

        if (project.ExistingHosts.Count > 0)
        {
            var hosts = project.ExistingHosts
                .Select(h => $"{Markup.Escape(h.Folder)} [grey]({Hosts.Label(h.Kind)})[/]");
            AnsiConsole.MarkupLine($"[grey]hosts[/]    {string.Join("[grey],[/] ", hosts)}");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>The project name for a new project.</summary>
    public static string AskProjectName(string? suggestion)
    {
        var prompt = new TextPrompt<string>("Project name:")
            .Validate(value => Hosts.SanitizeName(value) is null
                ? ValidationResult.Error("[red]needs at least one letter or digit[/]")
                : ValidationResult.Success());
        if (Hosts.SanitizeName(suggestion) is { } valid) prompt.DefaultValue(valid);
        return Hosts.SanitizeName(AnsiConsole.Prompt(prompt))!;
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
        AnsiConsole.MarkupLine("[grey]plan[/]");
        foreach (var description in descriptions)
            AnsiConsole.MarkupLine($"  [grey]-[/] {Markup.Escape(description)}");
        AnsiConsole.WriteLine();
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
