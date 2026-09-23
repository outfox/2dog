# 2dog.godotsharp

The 2dog fork's GodotSharp.dll and source generator, without native engine packages.
Game projects and reusable libraries should reference this package at the same version as
their 2dog host. The engine depends on an exact version of these bindings.

For a Godot.NET.Sdk game, explicitly set both properties before the first restore:

```xml
<PropertyGroup>
  <DisableImplicitGodotSharpReferences>true</DisableImplicitGodotSharpReferences>
  <DisableImplicitGodotGeneratorReferences>true</DisableImplicitGodotGeneratorReferences>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="2dog.godotsharp" Version="$(TwoDogVersion)" />
  <PackageReference Include="2dog.godotsharp.editor" Version="$(TwoDogVersion)"
                    Condition="'$(Configuration)' == 'Debug' Or '$(Configuration)' == 'Editor'" />
</ItemGroup>
```

Define TwoDogVersion in your shared Directory.Build.props. A library using Microsoft.NET.Sdk
usually only needs the core PackageReference. Editor APIs require 2dog.godotsharp.editor.
The source generator runs automatically for Godot.NET.Sdk projects. A custom game project
using Microsoft.NET.Sdk can set TwoDogEnableGodotSourceGenerators=true and supply GodotProjectDir.

When migrating, remove stock GodotSharp/GodotSharpEditor references in all game and library
projects (including dependencies you build yourself), restore, and rebuild. A host reference
does not change what a referenced game project compiles against. Older third-party packages
that depend on stock bindings need a compatible package release; mixed packages fail with TDG001.

Both bindings are checked by content hash before compilation and before publish trimming.
Copies of the packaged assemblies are accepted; stock or stale HintPath copies fail even when
their assembly versions match. Editor bindings and the engine must match the core package
version. These checks also run during publish --no-build.

The core package carries the fork's Release API, matching the engine's existing runtime API;
the editor package adds the fork's Debug editor API. Native debug/editor selection remains
controlled by TwoDogVariant. Packaging requires a matching fork build in TwoDogGodotSharpApiDir.

This package preserves the fork's trim annotations. It does not mark consumer assemblies as
trimmable, suppress IL2125, or establish that an application's reflection is safe to trim.
