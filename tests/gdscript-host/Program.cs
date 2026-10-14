using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using twodog.GdUnit;

// An executable integration proof for milestones 1 and 2. Its expected failing GDScript case must be reported
// as a failure by GdUnit; this executable succeeds only when every observed result matches the fixture contract.
try
{
    var source = Metadata("ProbeSource");
    var gdunit = Path.Combine(AppContext.BaseDirectory, "gdunit-runtime");
    var artifacts = Path.GetFullPath(args.Length == 0
        ? Path.Combine(source, "bin", "probe-artifacts", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"))
        : args.Single());
    if (Directory.Exists(artifacts)) throw new IOException($"Artifact directory must be new: {artifacts}");
    Directory.CreateDirectory(artifacts);
    RunFixture("pure", "Pure.gdtest", expectedPasses: 3, expectedFailures: 1, expectedSkips: 1);
    RunFixture("mixed", "Mixed.gdtest", expectedPasses: 1, expectedFailures: 0, expectedSkips: 0);
    Console.WriteLine($"PASS: isolated imports, GdUnit outcomes, async parameter rows, mixed scene, and engine teardown.\nArtifacts: {artifacts}");
    return 0;

    void RunFixture(string name, string host, int expectedPasses, int expectedFailures, int expectedSkips)
    {
        var output = Path.Combine(artifacts, name);
        var original = Path.Combine(output, "source");
        CopyFixture(Path.Combine(source, "fixtures", name), original);
        EmbeddedRunner.Import(original, Path.Combine(output, "source-import.log"));
        var sourceCache = File.ReadAllText(Path.Combine(original, ".godot", "global_script_class_cache.cfg"));
        Require(!sourceCache.Contains("GdUnitSuite", StringComparison.Ordinal), "A normal game import saw an ignored test suite.");
        Require(!Directory.EnumerateFiles(Path.Combine(original, host), "*.uid", SearchOption.AllDirectories).Any(),
            "A normal game import generated test UIDs.");
        Require(!Directory.EnumerateFiles(Path.Combine(original, host), "*.import", SearchOption.AllDirectories).Any(),
            "A normal game import generated test asset sidecars.");

        var before = Fingerprint(original);
        try
        {
            _ = new StagedProject(original, ["../outside"], gdunit, Path.Combine(gdunit, "bridge"), Path.Combine(output, "invalid"));
            throw new InvalidOperationException("Staging accepted a test root outside the game.");
        }
        catch (ArgumentException) { }
        // The stage itself lives beneath the ignored host, as it would in a real scaffold.
        var project = new StagedProject(original, [host + "/tests"], gdunit, Path.Combine(gdunit, "bridge"),
            Path.Combine(original, host, "obj", "run-1", "project"));
        Require(File.Exists(Path.Combine(project.Root, "addons", "gdUnit4", "LICENSE")), "The framework license is missing.");
        Require(!Directory.Exists(Path.Combine(project.Root, "addons", "gdUnit4", "test")), "Upstream self-tests leaked into the stage.");
        Require(!File.Exists(Path.Combine(project.Root, host, ".gdignore")), "The staged host is still hidden from Godot.");
        Require(!Directory.Exists(Path.Combine(project.Root, host, "tests", "ignored")), "A nested ignored directory became visible.");
        Require(!Directory.EnumerateFiles(project.Root, "*.csproj", SearchOption.AllDirectories).Any(),
            "Build project files should not be needed for resource loading.");
        EmbeddedRunner.Import(project.Root, Path.Combine(output, "stage-import.log"));
        var stageCache = File.ReadAllText(Path.Combine(project.Root, ".godot", "global_script_class_cache.cfg"));
        Require(stageCache.Contains(name == "pure" ? "IsolatedGdUnitSuite" : "MixedGdUnitSuite", StringComparison.Ordinal),
            "The private project did not register its test suite.");
        if (name == "pure")
        {
            Require(stageCache.Contains("IsolatedGameRules", StringComparison.Ordinal), "The game's global class was lost.");
            Require(File.Exists(Path.Combine(project.Root, host, "tests", "test_texture.svg.import")), "The test texture was not imported.");
            Require(File.Exists(Path.Combine(project.Root, host, "tests", "core_test.gd.uid")), "The test script UID was not generated.");
            Require(File.ReadAllText(Path.Combine(original, "game_rules.gd.uid")) == File.ReadAllText(Path.Combine(project.Root, "game_rules.gd.uid")),
                "The game script's UID changed during staging.");
        }
        var messages = EmbeddedRunner.Run(project, output);
        var discovered = messages.Where(item => item.GetProperty("kind").GetString() == "discovered")
            .Select(item => item.GetProperty("test")).ToArray();
        var events = messages.Where(item => item.GetProperty("kind").GetString() == "event")
            .Select(item => item.GetProperty("event")).ToArray();
        var results = events.Where(item => item.GetProperty("type").GetInt32() == 5).ToArray();
        Require(discovered.Length == expectedPasses + expectedFailures + expectedSkips, "Discovery returned the wrong number of cases.");
        Require(results.Length == discovered.Length, "Every discovered test must produce a terminal event.");
        Require(results.Count(item => Flag(item, "failed") || Flag(item, "errors")) == expectedFailures, "Unexpected failure count.");
        Require(results.Count(item => Flag(item, "skipped")) == expectedSkips, "Unexpected skip count.");
        Require(results.Count(item => !Flag(item, "failed") && !Flag(item, "errors") && !Flag(item, "skipped")) == expectedPasses,
            "Unexpected pass count.");
        Require(!results.Any(item => Flag(item, "warnings") || Flag(item, "flaky")), "The fixture produced warnings or retries.");
        Require(events.Any(item => item.GetProperty("type").GetInt32() == 8) && events.Any(item => item.GetProperty("type").GetInt32() == 9),
            "GdUnit did not complete its session lifecycle.");
        var completed = messages.Single(item => item.GetProperty("kind").GetString() == "completed");
        Require(completed.GetProperty("exit_code").GetInt32() == (expectedFailures == 0 ? 0 : 100), "Runner completion status disagrees with results.");
        foreach (var test in discovered)
        {
            Require(File.Exists(project.OriginalFile(test.GetProperty("source_file").GetString()!)), "Discovery did not map back to an original source file.");
            Require(test.GetProperty("line_number").GetInt32() > 0, "Discovery lost the source line.");
            var id = test.GetProperty("guid").GetString();
            Require(results.Count(item => item.GetProperty("guid").GetString() == id) == 1, "Result events lost their discovery identity.");
        }
        if (name == "pure")
        {
            var parameters = discovered.Where(item => item.GetProperty("test_name").GetString() == "test_async_parameter")
                .Select(item => item.GetProperty("attribute_index").GetInt32()).Order().ToArray();
            Require(parameters.SequenceEqual([0, 1]), "Parameterized async cases were not expanded separately.");
            var failure = results.Single(item => Flag(item, "failed"));
            Require(failure.GetProperty("reports").EnumerateArray().Any(report => report.GetProperty("line_number").GetInt32() > 0
                && report.GetProperty("message").GetString()!.Contains("2", StringComparison.Ordinal)), "The assertion failure lost its diagnostic.");
            var state = completed.GetProperty("fixture_state");
            Require(state.GetProperty("test_bodies").GetInt32() == 4, "A skipped body ran or an async body was lost.");
            Require(state.GetProperty("setups").GetInt32() == state.GetProperty("teardowns").GetInt32(), "Per-test teardown did not balance setup.");
            Require(state.GetProperty("suite_finished").GetBoolean(), "The suite teardown did not finish.");
            // A fresh stage must reflect deletions and preserve the earlier snapshot.
            File.WriteAllText(Path.Combine(original, host, "tests", "transient.txt"), "added asset");
            var next = new StagedProject(original, [host + "/tests"], gdunit, Path.Combine(gdunit, "bridge"),
                Path.Combine(original, host, "obj", "run-2", "project"));
            Require(File.Exists(Path.Combine(next.Root, host, "tests", "transient.txt")), "A new resource was missed.");
            File.Delete(Path.Combine(original, host, "tests", "transient.txt"));
            var last = new StagedProject(original, [host + "/tests"], gdunit, Path.Combine(gdunit, "bridge"),
                Path.Combine(original, host, "obj", "run-3", "project"));
            Require(!File.Exists(Path.Combine(last.Root, host, "tests", "transient.txt")), "A deleted resource survived staging.");
            Require(File.Exists(Path.Combine(next.Root, host, "tests", "transient.txt")), "Stages share writable source content.");
        }
        Require(before.OrderBy(item => item.Key).SequenceEqual(Fingerprint(original).OrderBy(item => item.Key)),
            "The test import or execution mutated source resources, settings, sidecars, or the source import cache.");
        if (name == "pure")
        {
            var pack = Path.Combine(output, "game.pck");
            EmbeddedRunner.Export(original, pack, Path.Combine(output, "export.log"));
            var inventory = EmbeddedRunner.ListPack(pack, Path.Combine(output, "pack-contents.txt"));
            Require(inventory.Contains("game_rules.gd", StringComparison.Ordinal), "The source game export is empty.");
            Require(!inventory.Contains(host, StringComparison.Ordinal) && !inventory.Contains("addons/gdUnit4", StringComparison.Ordinal)
                && !inventory.Contains("twodog_test_host", StringComparison.Ordinal), "Test resources leaked into the game export.");
        }
        Console.WriteLine($"PASS {name}: {discovered.Length} discovered; {expectedPasses} passed, {expectedFailures} expected failure(s), {expectedSkips} skipped; source unchanged.");
    }
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static string Metadata(string name) => Path.GetFullPath(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(item => item.Key == name).Value!);

static bool Flag(JsonElement item, string name) => item.GetProperty("statistics").TryGetProperty(name, out var value) && value.GetBoolean();

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static Dictionary<string, string> Fingerprint(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
    .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Contains("obj"))
    .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

static void CopyFixture(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    foreach (var directory in Directory.EnumerateDirectories(source))
        if (Path.GetFileName(directory) is not (".godot" or "bin" or "obj")) CopyFixture(directory, Path.Combine(destination, Path.GetFileName(directory)));
}
