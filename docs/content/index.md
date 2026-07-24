---
layout: home
title: 2dog
titleTemplate: :title - C# Godot Games on the Web

head:
  - - meta
    - name: title
      content: 2dog 🦴 Godot in .NET
  - - meta
    - name: description
      content: 2dog is Godot running in .NET! Export for Web, run unit tests, embed and automate. Keep your scenes, scripts, and Godot workflow.
  - - meta
    - property: og:type
      content: website
  - - meta
    - property: og:url
      content: https://2dog.dev
  - - meta
    - property: og:title
      content: 2dog 🦴 Godot in .NET

hero:
  name: 2dog
  text: Start, control, embed Godot in .NET.
  tagline: Keep your scenes, scripts, and editor workflow. 2dog lets .NET host Godot.
  image:
    src: /logo-animated.svg
    alt: a happy white dog smiling over the soft logotype text '2dog'
  actions:
    - theme: brand
      text: Read the Dogs
      link: /getting-started
    - theme: alt
      text: Quickstart (HTML5)
      link: /web
    - theme: alt
      text: Fetch on Github
      link: https://github.com/outfox/2dog

features:
  - title: 🌐 C# on the Web
    details: Just add <b>2dog</b> and export HTML5 and WASM for the web. No more waiting, Godot is home! 
    link: /convert
    linkText: Bring your project
  - title: 🎮 This is still Godot
    details: 2dog is a companion instead of a replacement. You will still edit and run with stock Godot.
    link: /project-layout
    linkText: See what changes
  - title: 🧪 Test like a Pro
    details: Load scenes, scripts, resources in xUnit or NUnit. Run Godot tests headlessly in your IDE or CI.
    link: /testing
    linkText: Test a scene
  - title: 🐕‍🦺 .NET holds the Leash
    details: Your application process starts Godot, drives each frame, and decides when the engine stops.
    link: /concepts
    linkText: How that works
---

## Start Where Your Project Is

2dog sits in a regular Godot project and nests desktop, browser, and test hosts inside it. These are invisible to the editor (via `.gdignore`) to avoid conflicts, and can be easily edited or removed.

:::: columns
::: column 🐕 I Already Have a Godot Game

2dog's cutest trick: convert in place and walk an existing C# game to the browser without a rewrite. Scenes, scripts, and assets stay where they are.

```shell
# no install needed, just use dnx (dotnet tool execute)
dnx 2dog convert path/to/MyGame
cd path/to/MyGame
dotnet run --project MyGame.2dog
```

[See exactly what conversion changes →](/convert)

:::
::: column 🥎 I'm Starting Fresh

Grow a new project from the template: a sample scene plus every host,
ready to run, test, and publish from the first minute.

```shell
dotnet new install 2dog
dotnet new 2dog -n MyGame
cd MyGame
dotnet run --project MyGame.2dog
```

[Explore the template options →](/templates)

:::
::::

Whichever trail you take, the project still opens in the regular Godot .NET editor afterwards. Authoring stays in Godot, while running, testing, and publishing move to `dotnet`.
