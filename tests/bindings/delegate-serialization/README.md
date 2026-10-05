This executable exercises the actual GodotSharp delegate serializer under normal .NET and NativeAOT. It checks
value and reference type arguments, nested generics, closures, native Godot targets, concurrent type serialization,
unknown generic types, and that the normal runtime does not retain editor assemblies in the AOT registry.

After building the managed packages and staging `godot/bin/GodotSharp/Api/Release`, publish this project in Release
with your desktop RID, then run it with the path to the release libgodot library as its only argument. Repeat with
`-p:PublishAot=true -warnaserror`. The desktop smoke workflow runs both versions.

To test freshly rebuilt bindings before repacking, set `GodotSharpPath` to the rebuilt `GodotSharp.dll`. An optional
`TwoDogEnginePath` points at a built `twodog.dll` and replaces the engine package reference for local testing.
