---
layout: home
title: 2dog
titleTemplate: :title - C# Godot Games on the Web

head:
  - - meta
    - name: title
      content: 2dog - C# Godot Games on the Web
  - - meta
    - name: description
      content: Bring an existing C# Godot game to the web. Keep your scenes, scripts, and Godot workflow.
  - - meta
    - property: og:type
      content: website
  - - meta
    - property: og:url
      content: https://2dog.dev
  - - meta
    - property: og:title
      content: 2dog - C# Godot Games on the Web

hero:
  name: 2dog
  text: Your C# Godot game. Now on the web.
  tagline: Keep your scenes, scripts, and editor workflow. 2dog lets .NET host Godot, then publishes your game as a static WebAssembly site.
  image:
    src: /logo-animated.svg
    alt: a happy white dog smiling over the soft logotype text '2dog'
  actions:
    - theme: brand
      text: Read the Dogs
      link: /getting-started
    - theme: alt
      text: Quickstart
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

Already have a Godot C# project? Bring it with you. Starting fresh? Grow a new
one. Both routes produce the same recommended layout and lead to desktop,
browser, and test hosts.

| | Existing Godot project | Fresh project |
| --- | --- | --- |
| **Best for** | Taking a current game further | Starting with every host ready |
| **First move** | Convert safely in place | Create from the project template |
| **Your work** | Game content stays in place | A sample scene is ready to run |
| **Follow the trail** | [Convert a project](/convert) | [Create from a template](/templates) |

The existing-project route gets a little more attention because that is where
2dog shows its best trick: taking a game you already know and opening a path to
the browser without a rewrite.

## What Actually Changes?

In regular Godot, the engine owns the process and calls your scripts. With
2dog, your .NET host owns the process and loads Godot as a library:

```text
Stock Godot                         2dog

Godot process                      Your .NET application
└── SceneTree                      └── 2dog Engine
    └── Your C# scripts                └── Godot + SceneTree
                                            └── Your C# scripts
```

That one inversion unlocks browser hosting, normal .NET entry points, and
first-class test runners. It does **not** ask you to relearn scenes, nodes,
signals, resources, or the Godot editor.

[Explore the architecture →](/concepts)

## Your First Trail

The introductory guide follows one continuous journey instead of dropping you
into engine code:

1. **Bring or create** a Godot C# project.
2. **Run it** through its desktop .NET host.
3. **Meet the layout** and understand which project owns what.
4. **Test it** headlessly with xUnit.
5. **Publish it** as a browser-ready static site.

[Start the guided walk →](/getting-started)

## Ready to Let It Run?

Once you know which trail you want, the complete first move is short:

::: code-group

```bash [🐕 Existing Project]
dnx 2dog convert path/to/MyGame
cd path/to/MyGame
dotnet run --project MyGame.2dog
```

```bash [🌱 Fresh Project]
dotnet new install 2dog
dotnet new 2dog -n MyGame
cd MyGame
dotnet run --project MyGame.2dog
```

:::

Your project still opens in the regular Godot .NET editor. When you are ready
for the browser, install the .NET WebAssembly workload once and follow the
[Web publishing guide](/web).

## Pick the Next Trick

- **I want the browser build:** [publish a C# Godot game to the web](/web).
- **I want confidence first:** [load and test scenes with xUnit](/testing).
- **I want the mental model:** [tour the recommended project layout](/project-layout).
- **I want full control:** [learn the engine lifecycle and main loop](/concepts).
- **Something growled:** [find a known issue or workaround](/known-issues/).
