---
title: WebXR Configuration
description: "MSBuild configuration for WebXR hosts, using the Web host's browser engine, pack, compression, and AOT settings."
---

# WebXR Configuration

Set properties in `MyGame.webxr.csproj`. WebXR uses the same
[browser build properties](./web#properties),
[precompression](./web#precompression), and
[trimming and Mono AOT settings](./web#trimming-and-aot) as the Web host.
The generated host defaults to Release.

`TwoDogWebVariant` selects debug for Debug and release for Release. Editor is
unsupported. `TwoDogWebExportPreset` defaults to `Web`, and
`TwoDogExportPack=false` uses an existing `wwwroot/godot.pck`.
`WasmEmitSymbolMap` defaults to `false` in Release and `true` otherwise.

```bash
dotnet publish MyGame.webxr -c Release -p:RunAOTCompilation=true
```

There are no additional WebXR-specific MSBuild properties. XR shaders and scene
setup belong to the Godot project; see the [WebXR host guide](/hosts/webxr).
