using System.IO.Enumeration;
using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.CommandLine;

namespace twodog.GdUnit;

internal sealed class GdUnitCommandLineOptions : ICommandLineOptionsProvider
{
    private string? _filter;
    public string Uid => "2dog.gdunit.options";
    public string Version => typeof(GdUnitCommandLineOptions).Assembly.GetName().Version!.ToString();
    public string DisplayName => "gdUnit selection";
    public string Description => "Filter GDScript tests by fully qualified name.";
    public Task<bool> IsEnabledAsync() => Task.FromResult(true);
    public IReadOnlyCollection<CommandLineOption> GetCommandLineOptions() =>
        [new("gdunit-filter", "Case-sensitive test name pattern; * matches any text and ? matches one character.", new ArgumentArity(1, 1), false)];
    public Task<ValidationResult> ValidateOptionArgumentsAsync(CommandLineOption commandOption, string[] arguments)
        => arguments.Length == 1 && !string.IsNullOrWhiteSpace(arguments[0])
            ? ValidationResult.ValidTask : ValidationResult.InvalidTask("Provide one nonempty gdUnit test name pattern.");
    public Task<ValidationResult> ValidateCommandLineOptionsAsync(ICommandLineOptions commandLineOptions)
    {
        _filter = commandLineOptions.TryGetOptionArgumentList("gdunit-filter", out var arguments) ? arguments.Single() : null;
        return ValidationResult.ValidTask;
    }
    public bool Matches(string name) => _filter is null || FileSystemName.MatchesSimpleExpression(_filter, name, ignoreCase: false);
}
