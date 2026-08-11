---
title: WebXR Host
description: "The webxr 2dog host: the browser host with the WebXR Layers polyfill prewired for VR - adding it, the game project's XR opt-in, and testing without a headset."
---

# WebXR Host

`MyGame.webxr` is a [browser host](./web) whose page shell vendors and loads the
[WebXR Layers polyfill](https://github.com/immersive-web/webxr-layers-polyfill) (Apache-2.0)
before `godot.js`:

```html
<script src="webxr-layers-polyfill.min.js"></script>
<script>new WebXRLayersPolyfill();</script>
<script src="godot.js"></script>
```

Godot renders WebXR through the [WebXR Layers API](https://www.w3.org/TR/webxrlayers-1/), which
desktop Chrome and the
[Immersive Web Emulator](https://chromewebstore.google.com/detail/immersive-web-emulator/cgffilbpcibhmcfbgggfhfolhkfbhmik)
do not implement natively (the Meta Quest browser does). The polyfill fills that gap; the vendored
copy must be 1.1.0 or newer - 1.0.3 allocates its layer textures with invalid WebGL formats.

## Adding the Host

The webxr host is opt-in and never part of the default set:

```bash
dotnet new 2dog -n MyGame --webxr true
```

For an existing project, `dnx 2dog add --webxr [folder]` - interactively it is the
"webxr - WebAssembly host with the WebXR Layers polyfill for VR" checkbox.

## XR in the Game Project

Godot's [WebXR interface](https://docs.godotengine.org/en/stable/tutorials/xr/setting_up_webxr.html)
is compiled into the web natives and registered on `XRServer` automatically - no host changes are
needed. The game project opts in with two pieces:

1. **Project settings**: enable XR shaders in `project.godot`:

   ```ini
   [xr]
   shaders/enabled.web=true
   ```

   (The `.web` feature-tag override avoids compiling the extra shader variants on desktop hosts.)
   Add an `XROrigin3D` with an `XRCamera3D` to your scene.

2. **A user gesture**: browsers only start XR sessions from a user gesture, and a Godot UI
   button press qualifies - no HTML button is required:

   ```csharp
   var webxr = (WebXRInterface)XRServer.FindInterface("WebXR");
   webxr.SessionSupported += (mode, supported) => _enterVrButton.Visible = supported;
   webxr.SessionStarted += () => GetViewport().UseXR = true;
   webxr.IsSessionSupported("immersive-vr");

   // In the button's Pressed handler:
   webxr.SessionMode = "immersive-vr";
   webxr.RequestedReferenceSpaceTypes = "bounded-floor, local-floor, local";
   webxr.RequiredFeatures = "local-floor";
   webxr.Initialize();
   ```

   On desktop hosts `FindInterface("WebXR")` returns `null` - guard and skip.

The showcase's `main.tscn` + `WebXR.cs` demonstrate the full pattern, and its `showcase.webxr`
host mirrors this one.

## Testing

WebXR needs a secure context: `localhost` works for development; anything else requires HTTPS.

- **Without a headset**: install the
  [Immersive Web Emulator](https://chromewebstore.google.com/detail/immersive-web-emulator/cgffilbpcibhmcfbgggfhfolhkfbhmik)
  extension in Chrome. The Enter-VR button appears, and clicking it enters emulated VR.
- **On a Quest** against a dev machine: either serve over HTTPS or use
  `adb reverse tcp:8080 tcp:8080` so the game stays on `localhost`.
- **Tools embedding the emulation runtime (IWER)**: `iwer`'s `installRuntime` must be called with
  `{ polyfillLayers: true }`, or pure-Layers apps render black.

## Everything Else

The [Program](./web#program), [project shape](./web#project-differences),
[host properties](./web#host-properties), and publishing are identical to the
[Browser host](./web):

```bash
dotnet publish MyGame.webxr
```

Publishing requires the wasm-tools workload; the published `MyGame.webxr/AppBundle/` directory is
a static site.
