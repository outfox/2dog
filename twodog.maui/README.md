# 2dog.maui

An embeddable `GodotView` for .NET MAUI on Windows, Android and iOS (experimental). Register
`builder.UseTwoDog()`, then add `new GodotView { Project = "MyGame" }` to your
page. Use `view.Dispatch(tree => ...)` to access Godot on its owning thread.

Windows presents through a WinUI SwapChainPanel. Vulkan writes a shared D3D11
texture, then D3D11 copies it to the composition swap chain. Both transfers
stay on the GPU. Auto mode falls back to CPU readback when GPU sharing is
unavailable. Set PresentationMode.Gpu to require Vulkan sharing or
PresentationMode.Cpu to select readback. ActivePresentationMode reports the
active path, and PresentationModeChanged announces changes. MAUI controls
can overlap the game in either mode.

Android embeds GodotFragment and its native SurfaceView. Godot owns rendering,
input, and lifecycle. It renders directly to the Android surface without copying
frames through .NET. Initialize AndroidHost in the MAUI Application before
creating the view. Never call Engine.Start() on Android.

One active view per process is supported. Android engine restart in the same
process is not supported. Listen to Started, QuitRequested, and Failed. Windows
forwards mouse, touch, and basic keyboard input; IME and mouse capture are not
implemented. Android uses Godot's native input handling.

iOS hosts Godot's own view controller (2dog.ios) as a child of the page's
view controller; Godot's display link runs on the main thread MAUI shares, so
Dispatch runs between frames. Register IosHost with the game's
TwoDogWebBoot.PluginsInitializer() in the app delegate before creating the
view. Building needs a Mac with Xcode. Never call Engine.Start() on iOS.

Scaffold with `dnx 2dog add --maui`. See https://2dog.dev/hosts/maui.
