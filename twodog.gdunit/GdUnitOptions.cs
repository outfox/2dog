namespace twodog.GdUnit;

/// <summary>Source project and private execution settings for a GDScript test host.</summary>
public sealed record GdUnitOptions
{
    /// <summary>Directory containing the game's project.godot.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Directories relative to the game, including tests beneath an ignored host.</summary>
    public IReadOnlyList<string> TestRoots { get; init; } = ["tests"];

    /// <summary>Parent for fresh stages, import logs, native logs and serialized events.</summary>
    public string ArtifactsDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "TestResults", "gdunit");

    /// <summary>Directory containing the built game assembly and its dependencies.</summary>
    public string ProjectAssemblyDirectory { get; init; } = AppContext.BaseDirectory;

    /// <summary>Maximum time allowed for the separate resource import process.</summary>
    public TimeSpan ImportTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum time spent pumping one embedded session, including discovery.</summary>
    public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
