---
title: WebGPU
description: "Configure 2dog's experimental WebGPU driver for Forward+ and Mobile on desktop and web, including browser fallback and adapter limits."
tagline: "Try WebGPU on desktop and web, and learn which adapters and features work."
keywords: "Godot WebGPU, Godot Forward+ browser, Godot C# WebGPU"
---

# WebGPU

WebGPU is opt-in. It supports the Forward+ and Mobile renderers on these targets:

- Windows, Linux and macOS, in single precision
- single-precision browser runtimes, including supported threaded packages

2dog does not change your existing renderer defaults. If you use WebGPU with
Android or double precision, you get a clear error.
Native threading remains supported. Headless imports and exports do not require
a WebGPU device. WebXR's immersive rendering is outside this experimental scope.

## Desktop

`--rendering-driver webgpu` selects Forward+ unless you also pass
`--rendering-method mobile`:

```bash
dotnet run --project MyGame.2dog -- --rendering-driver webgpu
```

For project settings, select both the renderer and the platform driver:

```ini
[rendering]

renderer/rendering_method="forward_plus"
rendering_device/driver="webgpu"
rendering_device/driver.windows="webgpu"
rendering_device/driver.linuxbsd="webgpu"
rendering_device/driver.macos="webgpu"
```

Configure `.web` and `.mobile` feature overrides explicitly when present.

Native Dawn loads only when selected. WebGPU requires macOS 13 or newer; other
renderers retain their existing runtime requirements. Desktop packages include
Dawn and the compiler sidecars and notices needed by the selected backend.

For a WebGPU desktop export, enable `shader_baker/enabled` in the preset and
select `webgpu` as that preset's project rendering driver. The baker uses the
preset's driver when writing the pack. Passing `--rendering-driver webgpu` at
launch can otherwise make the game compile its Forward+ shaders on the first
run. Dawn also caches device-specific pipelines on the player's machine.

## Browser requirements

Single-precision browser packages include WebGPU and WebGL. For threaded WebGPU,
use a 4.8 native release after `4.8.0.207` and set `WasmEnableThreads=true`.
The stock `4.8.0.207` package includes WebGPU only in its single-threaded
payload. The 4.7 threaded runtime and double-precision runtimes use WebGL.
WebGPU requires a secure context and browser support.

The project's `renderer/rendering_method.web` selects WebGPU for Forward+ and
Mobile, or OpenGL/WebGL for Compatibility (the default). See
[Forward+ and Mobile on the web](./web-renderers) for the manual
`project.godot` setting and
**shader baking**, which reduces first-load compilation time.

On Linux, if Firefox does not expose WebGPU, check `dom.webgpu.enabled` in
`about:config`; without WebGPU, the host starts Compatibility.

### Web host

To request WebGPU explicitly, configure `GODOT_CONFIG` in `wwwroot/index.html`
before you create `Engine`:

```javascript
'renderingDriver': 'webgpu',
'args': ['--rendering-driver', 'webgpu', '--rendering-method', 'forward_plus'],
```

Use `mobile` instead of `forward_plus` to request Mobile.

The shell requests a device before it initializes Godot. If one of these
conditions occurs, the shell logs a warning and starts with `opengl3` and
`gl_compatibility`, with the same runtime and game pack:

- WebGPU is unavailable.
- No adapter is available.
- The adapter or device request fails.

This fallback happens before startup; it does not recover from later device loss
or native renderer failures. Game content must also work with Compatibility.

To always use Compatibility, set `renderer/rendering_method.web` to
`"gl_compatibility"`, or override both command-line options:

```javascript
'renderingDriver': 'opengl3',
'args': ['--rendering-driver', 'opengl3', '--rendering-method', 'gl_compatibility'],
```

The `opengl3` driver uses WebGL 2 in browsers (OpenGL ES 3.0), not WebGL 3.

### Blazor

Set `RenderingDriver="webgpu"` on `GodotView` to select WebGPU Forward+; add
`--rendering-method mobile` to `Args` for Mobile. `--rendering-driver webgpu` in
`Args` also selects WebGPU when `RenderingDriver` is unset. Without either, the
project's web renderer setting decides.

If the browser has no WebGPU device, `GodotView` logs a warning and starts
`opengl3` with `gl_compatibility`, ignoring the renderer options in `Args`.
Later views reuse the same device. If initialization is cancelled, 2dog releases
a device that no view has claimed.

## Adapter and shader limits

Forward+ on WebGPU binds a compact scene: 4 lightmaps and 2 VoxelGIs per frame
instead of 8, with the lightmap data in a uniform buffer. That keeps the scene
shader within Safari's 44 sampled textures and Chrome on Metal's 10 storage
buffers per shader stage. Forward+ needs at least 40 sampled textures and 10
storage buffers per shader stage. On an adapter without them, Godot prints one
warning and runs Mobile; `RenderingServer.GetCurrentRenderingMethod()` then
returns `mobile`. A material can still exceed an adapter's limit with many
textures of its own; that material fails to compile and logs the limit.

SDFGI, VoxelGI and the compute depth of field read and write RGBA8, RGBA16 and
RGBA32 storage textures in place, which needs the
[`texture-formats-tier2`](https://gpuweb.github.io/gpuweb/#texture-formats-tier2)
feature. Chrome exposes it on desktop GPUs; Firefox and software adapters such
as lavapipe do not. Without it, Forward+ still runs: SDFGI and VoxelGI are
disabled with one warning when a scene asks for them, and depth of field uses
the raster passes of the Mobile renderer. SSIL needs 8-bit read/write storage,
which an adapter has without `texture-formats-tier1` (8-bit storage is promoted
to 32-bit) or with tier2; the rare adapter with tier1 but not tier2 disables SSIL
with one warning.

The backend has no image atomics, multiview or ray tracing. Read/write storage
textures require native support for their format; unsupported formats fail
shader creation. Use supported R32 formats or separate read and write resources.
Overlapping writable storage-buffer bindings are rejected instead of redirected
to dummy buffers. GPU timestamp and memory statistics are not implemented.
Translated WGSL is cached; GPU pipeline compilation still happens at runtime.

## Source builds

Normal `uv run poe build-all` enables WebGPU on supported configurations. Keep
the Emscripten SDK pinned to `EmscriptenVersion` from `Directory.Build.props`.

```text
uv run build-godot.py --webgpu auto
uv run build-godot.py --platform web --webgpu yes
```

Use `--webgpu no` to omit the driver. `--webgpu-native-path <sdk>` supplies an
existing native Dawn boundary SDK, including its runtime libraries and licenses.
Unsupported configurations reject `--webgpu yes`; `auto` preserves their renderer.
Browser packages supply the Emscripten port, link flags and a writable cache.
Override that cache with
[`TwoDogWebGpuCachePath`](/configuration/web#properties) when needed.

### Threaded browser builds

Set `WasmEnableThreads=true` when publishing with a threaded WebGPU package.

```bash
dotnet publish tests/webgpu-browser-smoke -c Release -p:WasmEnableThreads=true
node .github/scripts/serve-isolated.mjs tests/webgpu-browser-smoke/AppBundle 8000
```

For a source build, select the generated payload explicitly when publishing.
Use its absolute path with a trailing slash.

```bash
uv run build-godot.py --platform web --target template_release --web-threads yes --webgpu yes
dotnet publish tests/webgpu-browser-smoke -c Release -p:WasmEnableThreads=true \
  -p:TwoDogWebNativeDir="$PWD/godot/bin/web-threads/template_release/"
```

The .NET worker that runs Godot owns the WebGPU device and queue. The shell
transfers the game canvas to that worker after its device request succeeds.
Godot can then encode and submit frames directly to the canvas. Input and
layout stay on the browser main thread. Other workers send GPU operations to
the device owner, including while it waits for shader compilation. Hosts that
supply their own main-thread device keep that device and its ownership.

The image regression exercises this build with `?threads=1`. The host needs
cross-origin isolation for shared Wasm memory. The server above supplies those headers.
