# GD.Print Output Not Visible in Tests

When using `GD.Print` in your tests, the output may appear to be missing or invisible.

## Why This Happens

`GD.Print` writes to **stdout** via Godot's native `OS::print()` function. By default, `dotnet test` suppresses stdout output from the test host process. The output is there, but hidden.

Additionally, `GD.Print` output is global—it gets interleaved with Godot engine messages, fixture initialization logs, and other console output. There's no way to associate it with a specific test.

## Making GD.Print Visible

To see `GD.Print` output, run tests with verbose logging:

```bash
dotnet test --logger "console;verbosity=detailed"
```

You'll see output like this, with your `GD.Print` calls mixed into the stream:

```
Initializing Godot...
Godot project: /path/to/project
Starting Godot instance...
Engine: Godot instance created successfully!
Godot Engine v4.6.stable.mono.custom_build...
Engine: Godot started successfully!
Godot initialized successfully.
/root                              <-- Your GD.Print output
Shutting down Godot...
```

## Recommendation: Use ITestOutputHelper

For test logging, prefer xUnit's `ITestOutputHelper` over `GD.Print`:

```csharp
[Collection("GodotHeadless")]
public class MyTests(GodotHeadlessFixture godot, ITestOutputHelper output)
{
    [Fact]
    public void MyTest()
    {
        var node = godot.Tree.Root;

        // Instead of: GD.Print(node.GetPath());
        output.WriteLine(node.GetPath());
    }
}
```

Benefits of `ITestOutputHelper`:

- **Per-test capture**: Output is associated with each specific test
- **Visible on failure**: Output appears in test results when a test fails
- **IDE integration**: Works with test explorers that display test output
- **No noise**: Not interleaved with engine initialization messages

`GD.Print` is still useful for debugging game logic at runtime, but in tests it adds noise to stdout that gets mixed with everything else.
