---
title: 2dog version
description: "Reference for 2dog version: print the tool version and every package a scaffold references, checked against nuget.org."
---

# `2dog version`

Prints the tool version and every package a scaffold references, each checked
against nuget.org (best effort, 2.5 s). `--version` is the same, except
[under dnx](/dnx-2dog#under-dnx).

```bash
2dog version
```

```text
2dog :2dog-version:  https://2dog.dev

tool + packages  :2dog-version: ✅  2dog, 2dog.engine, 2dog.avalonia, 2dog.blazor, 2dog.xunit
native binaries  :natives-version: ✅  2dog.win-x64, 2dog.linux-x64, 2dog.osx-arm64, 2dog.browser-wasm, 2dog.tools
Godot SDK        :godot-version: ✅  Godot.NET.Sdk, GodotSharp
```

| Mark | Meaning |
| --- | --- |
| ✅ (`ok` when redirected) | The latest stable release |
| 🔄 (`new`) | A newer stable release exists |
| none | nuget.org could not be reached |
