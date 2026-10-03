---
title: WebXR
description: "Run Godot VR and AR in the browser with WebXR and the WebXR Layers polyfill."
---

# WebXR

The WebXR host is a `browser-wasm` host with Godot's WebXR capabilities enabled
and the WebXR Layers polyfill included.

## Use It

From your Godot project directory, run these commands. Accept `add`'s
installation offer if `wasm-tools` is missing:

```bash
dnx 2dog add --webxr
dotnet publish MyGame.webxr
```

Serve the site:

```bash
dnx dotnet-serve -d MyGame.webxr/AppBundle
```

## Set Up Your Scene

Enable web XR shaders in `project.godot`:

```ini
[xr]
shaders/enabled.web=true
```

Add an `XROrigin3D` with an `XRCamera3D`. Start Godot's `WebXRInterface` from a
button press and enable `Viewport.UseXR` when the session starts.
See the [showcase scene](https://github.com/outfox/2dog/tree/main/demos/showcase)
for an example.

Use an XR-capable browser and device, or the
[Immersive Web Emulator](https://chromewebstore.google.com/detail/immersive-web-emulator/cgffilbpcibhmcfbgggfhfolhkfbhmik).
Serve over HTTPS outside `localhost`. The [Web host's limitations](./web#limitations)
also apply.
