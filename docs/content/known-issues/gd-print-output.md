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

Enable detailed console logging:

```bash
dotnet test --logger "console;verbosity=detailed"
```

Your messages show up, mixed into the test-host stream with engine and fixture logs.

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
the IDE test explorer, with no engine noise around it. `GD.Print` stays what it
is, a runtime debugging aid.
