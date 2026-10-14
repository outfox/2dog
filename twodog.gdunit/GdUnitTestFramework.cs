using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.TestFramework;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.Requests;

namespace twodog.GdUnit;

internal sealed class GdUnitTestFramework(GdUnitOptions options, GdUnitCommandLineOptions commandLine) : ITestFramework, IDataProducer
{
    // Normal Engine instances share one load context. Serialize even if MTP sends overlapping requests in future.
    private static readonly SemaphoreSlim EngineGate = new(1);

    public string Uid => "2dog.gdunit";
    public string Version => typeof(GdUnitTestFramework).Assembly.GetName().Version!.ToString();
    public string DisplayName => "2dog gdUnit";
    public string Description => "Embedded GDScript tests with isolated resource imports.";
    public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];
    public Task<bool> IsEnabledAsync() => Task.FromResult(true);
    public Task<CreateTestSessionResult> CreateTestSessionAsync(CreateTestSessionContext context)
        => Task.FromResult(new CreateTestSessionResult { IsSuccess = true });
    public Task<CloseTestSessionResult> CloseTestSessionAsync(CloseTestSessionContext context)
        => Task.FromResult(new CloseTestSessionResult { IsSuccess = true });

    public Task ExecuteRequestAsync(ExecuteRequestContext context) => Task.Run(() => Execute(context));

    private void Execute(ExecuteRequestContext context)
    {
        var entered = false;
        var selected = new Dictionary<string, GdUnitTestCase>(StringComparer.Ordinal);
        var finished = new HashSet<string>(StringComparer.Ordinal);
        var reportedFailure = false;
        var session = context.Request switch
        {
            DiscoverTestExecutionRequest discover => discover.Session,
            RunTestExecutionRequest run => run.Session,
            _ => null
        };
        try
        {
            if (session is null) throw new NotSupportedException($"Unsupported gdUnit request: {context.Request.GetType().Name}");
            var filter = context.Request is DiscoverTestExecutionRequest discovery ? discovery.Filter : ((RunTestExecutionRequest)context.Request).Filter;
            var discoverOnly = context.Request is DiscoverTestExecutionRequest;
            EngineGate.Wait(context.CancellationToken);
            entered = true;
            var artifactDirectory = Path.Combine(options.ArtifactsDirectory, System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(artifactDirectory);
            var runtime = Path.Combine(AppContext.BaseDirectory, "gdunit-runtime");
            var project = new StagedProject(options.ProjectPath, options.TestRoots, runtime, Path.Combine(runtime, "bridge"),
                Path.Combine(artifactDirectory, "project"), pauseAfterDiscovery: true, context.CancellationToken);
            EmbeddedRunner.Import(project.Root, Path.Combine(artifactDirectory, "import.log"), context.CancellationToken,
                options.ImportTimeout, options.ProjectAssemblyDirectory);
            var messages = EmbeddedRunner.Run(project, artifactDirectory, tests =>
            {
                foreach (var raw in tests)
                {
                    var test = GdUnitTestCase.From(raw, project);
                    if (!test.Matches(filter) || !commandLine.Matches(test.FullyQualifiedName)) continue;
                    selected.Add(test.Guid, test);
                    if (discoverOnly) Publish(test.Node(DiscoveredTestNodeStateProperty.CachedInstance));
                }
                return discoverOnly ? null : selected.Keys.ToArray();
            }, message =>
            {
                switch (message.GetProperty("kind").GetString())
                {
                    case "event" when !discoverOnly:
                        var result = message.GetProperty("event");
                        var type = result.GetProperty("type").GetInt32();
                        if (type is 4 or 5 && selected.TryGetValue(result.GetProperty("guid").GetString()!, out var test))
                        {
                            if (type == 4) Publish(test.Node(InProgressTestNodeStateProperty.CachedInstance));
                            else
                            {
                                if (!finished.Add(test.Guid)) throw new InvalidDataException("gdUnit reported a test result twice.");
                                var diagnostic = Diagnostic(result, test.SourceFile);
                                var state = Outcome(result, diagnostic);
                                reportedFailure |= state is FailedTestNodeStateProperty or ErrorTestNodeStateProperty;
                                var duration = TimeSpan.FromMilliseconds(result.GetProperty("statistics").GetProperty("elapsed_time").GetDouble());
                                var end = DateTimeOffset.UtcNow;
                                Publish(test.Node(state, new TimingProperty(new TimingInfo(end - duration, end, duration))));
                            }
                        }
                        else if (type == 3 && HasProblem(result))
                        {
                            reportedFailure = true;
                            var resource = result.GetProperty("resource_path").GetString()!;
                            PublishHostError(new InvalidOperationException(Diagnostic(result, project.OriginalFile(resource))),
                                "gdunit:suite-error:" + Uri.EscapeDataString(resource));
                        }
                        break;
                }
            }, context.CancellationToken, options.SessionTimeout, options.ProjectAssemblyDirectory);
            if (!discoverOnly && selected.Keys.Any(id => !finished.Contains(id)))
                throw new InvalidDataException("gdUnit completed without reporting every selected test.");
            var completion = messages.Single(message => message.GetProperty("kind").GetString() == "completed");
            if (completion.GetProperty("exit_code").GetInt32() != 0 && !reportedFailure)
                throw new InvalidDataException("gdUnit reported a session failure without a test failure.");
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            var pending = selected.Values.Where(test => !finished.Contains(test.Guid)).ToArray();
            foreach (var test in pending)
                Publish(test.Node(new ErrorTestNodeStateProperty(error)));
            if (pending.Length == 0) PublishHostError(error);
        }
        finally
        {
            if (entered) EngineGate.Release();
            context.Complete();
        }

        void Publish(TestNode node) => context.MessageBus.PublishAsync(this,
            new TestNodeUpdateMessage(session?.SessionUid ?? throw new NotSupportedException("Unknown test session."), node)).GetAwaiter().GetResult();

        void PublishHostError(Exception error, string uid = "gdunit:host-error") => Publish(new TestNode
        {
            Uid = new TestNodeUid(uid),
            DisplayName = "gdUnit host",
            Properties = new PropertyBag(new ErrorTestNodeStateProperty(error))
        });
    }

    private static TestNodeStateProperty Outcome(JsonElement result, string diagnostic)
    {
        if (Flag(result, "errors") || Flag(result, "warnings") || Orphans(result)) return new ErrorTestNodeStateProperty(diagnostic);
        if (Flag(result, "failed")) return new FailedTestNodeStateProperty(diagnostic);
        if (Flag(result, "skipped")) return new SkippedTestNodeStateProperty(diagnostic);
        return PassedTestNodeStateProperty.CachedInstance;
    }

    private static bool HasProblem(JsonElement result) => Flag(result, "failed") || Flag(result, "errors") || Flag(result, "warnings") || Orphans(result);
    private static bool Orphans(JsonElement result) => result.GetProperty("statistics").TryGetProperty("orphan_nodes", out var count) && count.GetInt32() > 0;
    private static bool Flag(JsonElement result, string flag) => result.GetProperty("statistics").TryGetProperty(flag, out var value) && value.GetBoolean();

    private static string Diagnostic(JsonElement result, string sourceFile) => string.Join(Environment.NewLine,
        result.GetProperty("reports").EnumerateArray().Select(report =>
        {
            var message = Regex.Replace(report.GetProperty("message").GetString()!, @"\[/?(?:color(?:=[^\]]*)?|b|i|u|s|font_size(?:=[^\]]*)?)\]", "");
            return $"{sourceFile}:{report.GetProperty("line_number").GetInt32()}: {message}";
        }));
}
