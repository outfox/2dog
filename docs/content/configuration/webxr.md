---
title: WebXR Configuration
description: "MSBuild configuration for WebXR hosts, using the Web host's browser engine, pack, compression, and AOT settings."
tagline: "Learn how to configure your WebXR builds for VR and AR in the browser."
---

# WebXR Configuration

Set properties in `MyGame.webxr.csproj`. WebXR uses the same
[browser build properties](./web#properties),
[precompression](./web#precompression), and
[trimming and Mono AOT settings](./web#trimming-and-aot) as the Web host.
The generated host defaults to Release.

```bash
dotnet publish MyGame.webxr -c Release -p:RunAOTCompilation=true
```

WebXR has no MSBuild properties of its own. XR shaders and scene
setup belong to the Godot project; see the [WebXR host guide](/hosts/webxr).
