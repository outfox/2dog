---
title: GD.Print in Tests
description: "Why GD.Print output is hidden during dotnet test, how to make it visible, and why ITestOutputHelper is the recommended pattern."
---

# GD.Print Output Not Visible in Tests

`GD.Print` prints nothing under `dotnet test`. The output exists; the runner hides it.

## Why This Happens

`GD.Print` writes to stdout through Godot's native `OS::print()`. `dotnet test`
hides test-host stdout by default. The stream is global, so output also mixes
with engine and fixture logs rather than belonging to a specific test.

## Making GD.Print Visible

Scaffolded test hosts run on Microsoft.Testing.Platform (MTP), so each one is
an executable. Run it directly to see its stdout in the terminal:

```bash
dotnet run --project MyGame.xunit
```

Your messages show up, mixed into the test-host stream with engine and fixture
logs. VSTest's `--logger "console;verbosity=detailed"` does not apply under
MTP; `dotnet test --output Detailed` lists every test result, not only
failures.

## Use ITestOutputHelper instead

For anything a test wants to say, use xUnit's `ITestOutputHelper`:

```csharp
[Collection<HeadlessCollection>]
public class MyTests(HeadlessFixture godot, ITestOutputHelper output)
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

The output then belongs to that test: it lands in the failure report and in
the IDE test explorer, with no engine noise around it. To watch it while
tests run, pass xUnit's `--show-live-output on`:

```bash
dotnet test --project MyGame.xunit --show-live-output on
```

`GD.Print` stays what it is, a runtime debugging aid.
