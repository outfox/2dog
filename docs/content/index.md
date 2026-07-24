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
  tagline: Keep your scenes, scripts, and editor workflow. 2dog lets .NET host Godot, then publishes your game as a static WebAssembly site.
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
  - title: 🌐 Go from Godot to the Web
    details: Convert an existing C# project in place, then publish it as a static site with dotnet publish.
    link: /convert
    linkText: Bring your project
  - title: 🎮 Keep the Godot You Know
    details: Your scenes, resources, C# scripts, signals, exports, and editor workflow stay familiar.
    link: /project-layout
    linkText: See what changes
  - title: 🧪 Test the Whole Game
    details: Load real scenes through xUnit and run Godot headlessly in local builds or CI.
    link: /testing
    linkText: Test a scene
  - title: 🔄 Let .NET Hold the Leash
    details: Your .NET process starts Godot, drives each frame, and decides when the engine stops.
    link: /concepts
    linkText: Learn how it works
---

## Start Where Your Project Is

Two trails lead in, and both end at the same place: a regular Godot project
with desktop, browser, and test hosts nested inside it, invisible to the
editor.

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

Whichever trail you take, the project still opens in the regular Godot .NET editor afterwards  –  authoring stays in Godot, while running, testing, and publishing move to `dotnet`.

## Who Holds the Leash?

Two process trees, one difference  –  who starts whom:

:::: columns
::: column 🤖 Stock: Godot as we know it

The engine owns the process. Your C# scripts ride along inside the SceneTree
and run when Godot calls them.

```text
Godot process
└── GodotSharp SDK
    └── SceneTree
    └── Your Godot C# scripts
```

:::
::: column 🐕‍🦺 With 2dog: .NET is your service (dog)

Your .NET application owns the process and loads Godot as a library  –  with
room for any other .NET code right beside it.

```text
.NET application or service
├── 2dog Engine (libgodot)
│   └── SceneTree
│   └── Your Godot C# scripts
└── More C# Code (whole .NET ecosystem - dream big!)
```

:::
::::

That one inversion is the whole trick. It unlocks browser publishing, normal
.NET entry points, and first-class test runners  –  while scenes, nodes, signals,
resources, and the Godot editor all stay exactly as you know them.

[Explore the architecture →](/concepts)


## Your First Trail

The introductory guide follows one continuous walk instead of dropping you
into engine code:

1. **Bring or create** a Godot C# project.
2. **Run it** through its desktop .NET host.
3. **Meet the layout** and learn which project owns what.
4. **Test it** headlessly with xUnit.
5. **Publish it** as a browser-ready static site.

[Start the guided walk →](/getting-started)

## Pick the Next Trick

- **I want the browser build:** [publish a C# Godot game to the web](/web)  – 
  one workload install, one `dotnet publish`.
- **I want confidence first:** [load and test scenes with xUnit](/testing).
- **I want the mental model:** [tour the recommended project layout](/project-layout).
- **I want full control:** [learn the engine lifecycle and main loop](/concepts).
- **Something growled:** [find a known issue or workaround](/known-issues/).
