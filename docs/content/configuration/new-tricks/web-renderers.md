---
title: Forward+ and Mobile Renderers on the Web
description: "Use Godot's Forward+ or Mobile renderer instead of Compatibility on the web with 2dog, and bake shaders to reduce first-load compilation time."
tagline: "Swap Compatibility for Forward+ or Mobile on the web, and bake shaders for a faster load."
keywords: "Godot Forward+ web, Godot Mobile renderer web, Godot shader baking, Godot C# web export"
---

# Forward+ and Mobile Renderers on the Web

Official Godot renders web exports with the Compatibility renderer only. 2dog
also brings the Forward+ and Mobile renderers to the browser, through its
experimental [WebGPU driver](./webgpu).

| Renderer | On the web | Graphics API |
|---|---|---|
| Compatibility | Official Godot and 2dog; the default | WebGL |
| Mobile | 2dog only | WebGPU |
| Forward+ | 2dog only | WebGPU |

New template projects keep using Compatibility until you opt in. The showcase
uses Forward+.

Browsers without WebGPU still [fall back to Compatibility](./webgpu#web-host),
so your game content must also work with that renderer.

## Replace Compatibility on the web

Edit `project.godot` manually. In its `[rendering]` section, usually near the
bottom of the file, set the web renderer to `forward_plus` or `mobile`:

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

## Choosing between Forward+ and Mobile

Forward+ features depend on the adapter. On adapters that cannot run it, 2dog
falls back to Mobile. See
[adapter and shader limits](./webgpu#adapter-and-shader-limits) for that
fallback and the restrictions on SDFGI, VoxelGI, depth of field, and SSIL.
