namespace twodog.cli;

internal enum Severity
{
    Pass,
    Info,
    Warn,
    Fail,
}

/// <summary>How a fix may be applied: Safe under --fix, Announced under --fix-all or when picked, Manual never.</summary>
internal enum FixClass
{
    Safe,
    Announced,
    Manual,
}

internal static class FixClasses
{
    /// <summary>The doctor invocation that applies fixes of this class.</summary>
    public static string Command(FixClass fixClass) => fixClass == FixClass.Safe ? "2dog doctor --fix" : "2dog doctor --fix-all";

    public static string Label(FixClass fixClass) => fixClass == FixClass.Safe ? "safe" : "announced";
}

internal enum Category
{
    Environment,
    Layout,
    GameProject,
    Hosts,
    Solution,
    Versions,
    Presets,
    GodotSettings,
}

internal static class Categories
{
    public static string Label(Category category) => category switch
    {
        Category.Environment => "environment",
        Category.Layout => "layout",
        Category.GameProject => "game csproj",
        Category.Hosts => "hosts",
        Category.Solution => "solution",
        Category.Versions => "versions",
        Category.Presets => "presets",
        Category.GodotSettings => "godot settings",
        _ => category.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// An automated repair for a finding. Findings sharing a key are fixed by one write (one csproj patch covers
/// several properties), so the planner applies each key once.
/// </summary>
internal sealed record Fix(string Key, FixClass Class, string Description, Action Apply)
{
    public string Tag => FixClasses.Label(Class);
}

/// <summary>One check outcome: what was looked at, how bad it is, what to do about it.</summary>
internal sealed record Finding(
    string Id,
    Category Category,
    Severity Severity,
    string Title,
    string? Detail = null,
    string? Remedy = null,
    string? Path = null,
    Fix? Fix = null)
{
    public static Finding Pass(string id, Category category, string title) => new(id, category, Severity.Pass, title);

    /// <summary>The remedy a non-interactive run prints: the doctor command for automated fixes, the text otherwise.</summary>
    public string? Advice => Fix is { Class: not FixClass.Manual } fix ? Remedy ?? FixClasses.Command(fix.Class) : Remedy;
}
