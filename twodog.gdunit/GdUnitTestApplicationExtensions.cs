using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Capabilities.TestFramework;

namespace twodog.GdUnit;

/// <summary>Registers the embedded GDScript test framework with Microsoft.Testing.Platform.</summary>
public static class GdUnitTestApplicationExtensions
{
    /// <summary>Adds gdUnit discovery and execution to a test application.</summary>
    public static ITestApplicationBuilder AddGdUnit(this ITestApplicationBuilder builder, GdUnitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProjectPath);
        if (options.TestRoots.Count == 0) throw new ArgumentException("Select at least one test root.", nameof(options));
        if (options.ImportTimeout <= TimeSpan.Zero || options.SessionTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Host timeouts must be positive.", nameof(options));
        var resolved = options with
        {
            ProjectPath = Path.GetFullPath(options.ProjectPath),
            ArtifactsDirectory = Path.GetFullPath(options.ArtifactsDirectory),
            ProjectAssemblyDirectory = Path.GetFullPath(options.ProjectAssemblyDirectory),
            TestRoots = options.TestRoots.ToArray()
        };
        var commandLine = new GdUnitCommandLineOptions();
        builder.CommandLine.AddProvider(() => commandLine);
        return builder.RegisterTestFramework(_ => new TestFrameworkCapabilities(), (_, _) => new GdUnitTestFramework(resolved, commandLine));
    }
}
