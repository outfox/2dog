---
title: WebXR
description: "Run Godot VR and AR in the browser with WebXR and the WebXR Layers polyfill."
tagline: "Learn how to bring Godot VR and AR to the browser."
keywords: "Godot WebXR C#, Godot VR in the browser, WebXR"
---

# WebXR

The WebXR host is a `browser-wasm` host with Godot's WebXR capabilities enabled
and the WebXR Layers polyfill included. It is single-threaded: `--web-threads`
applies only to Web and Blazor hosts.

See [WebXR Configuration](/configuration/webxr) for build and publish settings.

## Use It

Run these commands from your Godot project directory. If `wasm-tools` is
missing, accept the offer from `add` to install it:

```bash
dnx 2dog add --webxr
dotnet publish MyGame.webxr
```

Serve the site:

```bash
dnx dotnet-serve -- -d MyGame.webxr/AppBundle
```

## Set Up Your Scene

Enable web XR shaders in `project.godot`:

```ini
[xr]
shaders/enabled.web=true
```

Add an `XROrigin3D` with an `XRCamera3D`. Start Godot's `WebXRInterface` when the
user presses a button. When the session starts, enable `Viewport.UseXR`.
See the [showcase scene](https://github.com/outfox/2dog/tree/main/demos/showcase)
for an example.

Use an XR-capable browser and device, or the
[Immersive Web Emulator](https://chromewebstore.google.com/detail/immersive-web-emulator/cgffilbpcibhmcfbgggfhfolhkfbhmik).
If you do not serve the site from `localhost`, use HTTPS. The
[Web host's limitations](./web#limitations) also apply.
