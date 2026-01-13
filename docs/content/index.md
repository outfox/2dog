---
layout: home
title: 2dog
titleTemplate: :title – Godot in .NET

head:
  - - meta
    - name: title
      content: 2dog – Godot in .NET
  - - meta
    - name: description
      content: Embed Godot Engine in your .NET applications. Full engine control, xUnit testing, and CI/CD support.
  - - meta
    - property: og:type
      content: website
  - - meta
    - property: og:url
      content: https://2dog.dev
  - - meta
    - property: og:title
      content: 2dog – Godot in .NET

hero:
  name: "What if Godot..."
  text: "...but backward?"
  tagline: "Start &amp; control Godot from .NET code."
  image:
    src: /logo.svg
    alt: a happy white dog smiling over the soft logotype text '2dog'
  actions:
    - theme: brand
      text: Get Started
      link: /getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/outfox/2dog

features:
  - title: 🎮 Full Godot Power
    details: Access the complete GodotSharp API — scenes, physics, rendering, audio, input — everything Godot can do.
  - title: 🔄 Inverted Control
    details: Your .NET process controls Godot, not the other way around. Start, iterate, and stop the engine when you decide.
  - title: 🧪 First-Class Testing
    details: Built-in xUnit fixtures for testing Godot code. Run headless in CI/CD pipelines.
  - title: 📦 Simple Integration
    details: Install via NuGet. Native libraries download automatically. Works with standard .NET tooling.

---


::: code-group

```csharp [🎮 Game Example]
using twodog;

using var engine = new Engine("myapp", "./project");
using var godot = engine.Start();

// Load a scene
var scene = GD.Load<PackedScene>("res://game.tscn");
engine.Tree.Root.AddChild(scene.Instantiate());

// Run the main loop
while (!godot.Iteration())
{
    // Your code here — every frame
}
```

```csharp [🧪 Unit Test Example]
using twodog.xunit;

[Collection("GodotHeadless")]
public class GodotSceneTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void LoadScene_ValidPath_Succeeds()
    {
        var scene = GD.Load<PackedScene>("res://game.tscn");
        var instance = scene.Instantiate();
        
        godot.Tree.Root.AddChild(instance);
        
        Assert.NotNull(instance.Parent);
    }
}
```

:::
