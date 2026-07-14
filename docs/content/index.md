---
layout: home
title: 2dog
titleTemplate: :title - Godot in .NET

head:
  - - meta
    - name: title
      content: 2dog - Godot in .NET
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
      content: 2dog - Godot in .NET

hero:
  image:
    src: /logo-animated.svg
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
    details: Access the complete GodotSharp API  -  scenes, physics, rendering, audio, input  -  everything Godot can do.
  - title: 🔄 Inverted Control
    details: Your .NET process controls Godot, not the other way around. Start, iterate, and stop the engine when you decide.
  - title: 🧪 First-Class Testing
    details: Built-in xUnit fixtures for testing Godot code. Run headless in CI/CD pipelines.
  - title: 🌐 C# Games on the Web
    details: Publish your C# game as a static site with one dotnet publish  -  something stock Godot + C# can't do today.
    link: /web
    linkText: Run in the browser

---

## Installation

::: code-group

```bash [🤖 Existing Project]
# Convert in place - scaffolds the 2dog hosts around your Godot project
# (no install: dnx runs the tool straight from NuGet)
dnx 2dog.cli convert path/to/MyGame

cd path/to/MyGame

# Run on desktop/in Console as .NET application
dotnet run --project MyGame.2dog

# You can still just run or edit with Godot
# (the project root IS still the Godot project)
godot-mono --editor .

# Publish for the browser as a static site
# (one-time: dotnet workload install wasm-tools)
dotnet publish MyGame.web -c Release
dotnet serve --directory MyGame.web/AppBundle
```

```bash [🌱 Fresh Project]
# "Install" 2dog: register the project template
dotnet new install 2dog

# Create your Project
dotnet new 2dog -n MyGame

cd MyGame

# Assets are imported automatically during build
dotnet run --project MyGame.2dog

# Optionally, open and edit the project in the Godot Editor
# (the project root IS still the Godot project)
godot-mono --editor .
```

```bash [📦 Just the Packages]
dotnet add package 2dog
dotnet add package 2dog.xunit
```

:::



## All right, let's cook! 

If you want to do more, like control the engine, build a whole separate app or server around it, **2dog** throws you many bones! 🦴 Here are a few starters!

::: code-group

```csharp [🎮 Basic Example]
using twodog;

using var engine = new Engine("myapp", "./project");
using var godot = engine.Start();

// Load a scene
var scene = GD.Load<PackedScene>("res://game.tscn");
engine.Tree.Root.AddChild(scene.Instantiate());

// Run the main loop
while (!godot.Iteration())
{
    // Your code here  -  every frame
}
```

```csharp [🧪 Unit Test Example]
using twodog.fixture;
using twodog.xunit;

[Collection<GodotHeadlessCollection>]
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

```csharp [🔨 Tool Example]
// With the editor native variant (TwoDogVariant=editor),
// TOOLS_ENABLED code paths and [Tool] scripts are active.

using twodog;

using var engine = new Engine("tool", "./project", "--headless");
using var godot = engine.Start();

// Drive the scene tree from your own code:
// batch processing, validation, custom tooling.
var scene = GD.Load<PackedScene>("res://main.tscn");
godot.Tree.Root.AddChild(scene.Instantiate());
```

:::
