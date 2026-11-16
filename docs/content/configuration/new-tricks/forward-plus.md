---
title: Forward+
description: "Enable Godot's Forward+ renderer on the web with 2dog and bake shaders to reduce first-load compilation time."
tagline: "Bring Forward+ to the web and bake shaders for a faster first load."
keywords: "Godot Forward+ web, Godot shader baking, Godot C# web export"
---

# Forward+

2dog brings Godot's Forward+ renderer to the web through its experimental
[WebGPU driver](./webgpu). Mobile is also supported; new template projects
use Compatibility by default. The showcase uses Forward+.

## Enable Forward+ on the web

Edit `project.godot` manually. In its `[rendering]` section, usually near the
bottom of the file, set the web renderer:

```ini
[rendering]

renderer/rendering_method.web="forward_plus"
```

The Godot Editor does not offer this web override in its renderer selector.
The setting sticks unless you explicitly clear it in Project Settings.

During bundling, Forward+ and Mobile select WebGPU when the native payload
includes it; other settings select WebGL. Use a single-precision,
single-threaded browser payload. See [WebGPU](./webgpu) for browser support,
host overrides, fallback behavior, and adapter limits.

## Bake shaders before exporting

**Enable shader baking in each web export preset.** Browsers compile both the
WASM and the shaders. Without baking, translating every Forward+ shader
variant on the browser's main thread can take 5-10 minutes on first load and
trigger an unresponsive-page prompt. Baking ships the translated shaders in
the pack to reduce that work; GPU pipeline compilation still happens at runtime.

In `export_presets.cfg`, enable baking in the options section of each web
preset. Replace `3` with the number of your web export preset, and repeat for
any additional web presets:

```ini
[preset.3.options]

shader_baker/enabled=true
```

Publishing then runs the headless export with a window-less rendering device
(Vulkan, or Metal on macOS), so it needs a GPU or a software adapter such as
lavapipe, but no display. Baked shaders make the pack larger, by about 18 MB
compressed for a small Forward+ project.

Forward+ features depend on the adapter. See
[adapter and shader limits](./webgpu#adapter-and-shader-limits) for the Mobile
fallback and restrictions on SDFGI, VoxelGI, depth of field, and SSIL.
