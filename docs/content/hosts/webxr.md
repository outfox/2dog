---
title: WebXR Host
description: "The WebXR host adds a prewired WebXR Layers polyfill to the browser host for browser-based VR and AR."
---

# WebXR Host

`MyGame.webxr` is the [Browser host](./web) with the WebXR Layers polyfill
vendored and loaded before Godot. Use it for browser VR or AR when targets may
not implement the Layers API themselves.

## Use It

The host is opt-in:

```bash
dnx 2dog add --webxr
dotnet new 2dog -n MyGame --webxr true
```

Its build and local serving workflow is the same as the browser host; see
[Web / Browser (WASM)](/web).

## Capabilities

- Uses Godot's built-in WebXR interface.
- Works with native WebXR Layers implementations, including Meta Quest Browser.
- Adds Layers support for desktop Chrome and the Immersive Web Emulator.
- Keeps the normal non-XR viewport available before and after an XR session.

## How It Works

The page shell loads and instantiates version 1.1.0 or newer of the
[WebXR Layers polyfill](https://github.com/immersive-web/webxr-layers-polyfill)
before `godot.js`:

```html
<script src="webxr-layers-polyfill.min.js"></script>
<script>if (navigator.xr) new WebXRLayersPolyfill();</script>
<script src="godot.js"></script>
```

The `navigator.xr` guard allows the page to load in browsers where no XR
session is available. The polyfill is necessary because Godot renders through
the [WebXR Layers API](https://www.w3.org/TR/webxrlayers-1/), which some desktop
browsers and emulators do not implement natively.

## Project Setup

Enable web-only XR shaders in `project.godot`, then add an `XROrigin3D` with an
`XRCamera3D` to the scene:

```ini
[xr]
shaders/enabled.web=true
```

Start the session from a user gesture, such as a Godot button press:

```csharp
if (XRServer.FindInterface("WebXR") is not WebXRInterface webxr)
    return;

webxr.SessionSupported += (mode, supported) => _enterVrButton.Visible = supported;
webxr.SessionStarted += () => GetViewport().UseXR = true;
webxr.SessionEnded += () => GetViewport().UseXR = false;
webxr.IsSessionSupported("immersive-vr");

// In the button's Pressed handler:
webxr.SessionMode = "immersive-vr";
webxr.RequestedReferenceSpaceTypes = "bounded-floor, local-floor, local";
webxr.RequiredFeatures = "local-floor";
webxr.OptionalFeatures = "bounded-floor";
webxr.Initialize();
```

The pattern guard also makes this game code safe on desktop hosts, where no
`WebXR` interface is registered.

## Testing

- Install the
  [Immersive Web Emulator](https://chromewebstore.google.com/detail/immersive-web-emulator/cgffilbpcibhmcfbgggfhfolhkfbhmik)
  in Chrome to test without a headset.
- Use HTTPS outside `localhost`; browsers require a secure context for WebXR.
- For a Quest connected to a development machine, use HTTPS or
  `adb reverse tcp:8080 tcp:8080` to preserve a `localhost` origin.
- Tools using IWER must call `installRuntime` with `{ polyfillLayers: true }`.

## Limitations

- The [Browser host limitations](./web#limitations) also apply.
- XR sessions require browser and device support plus a user gesture.
- Polyfill versions before 1.1.0 allocate layer textures with invalid WebGL
  formats and are unsupported.
