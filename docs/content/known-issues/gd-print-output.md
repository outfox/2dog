---
title: GD.Print in Tests
description: "Why GD.Print output is hidden during dotnet test, how to make it visible, and why ITestOutputHelper is the recommended pattern."
---

# GD.Print Output Not Visible in Tests

`GD.Print` output may appear to be missing during tests.

## Why This Happens

`GD.Print` writes to stdout through Godot's native `OS::print()`. `dotnet test`
hides test-host stdout by default. The stream is global, so output also mixes
with engine and fixture logs rather than belonging to a specific test.

## Making GD.Print Visible

Enable detailed console logging:

```bash
dotnet test --logger "console;verbosity=detailed"
```

Your messages will appear mixed into the test-host stream.

## Recommendation: Use ITestOutputHelper

For test logging, prefer xUnit's `ITestOutputHelper` over `GD.Print`:

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

`ITestOutputHelper` associates output with the test, includes it in failure
results, and works with IDE test explorers without mixing in engine logs. Keep
`GD.Print` for runtime debugging rather than test logging.
