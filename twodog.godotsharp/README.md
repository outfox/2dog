# 2dog.godotsharp

The 2dog fork's managed bindings and source generator, without native engine dependencies.
Use the same version as the host's 2dog.engine package.

`2dog new`, `2dog add`, and `2dog update` configure game references automatically. A game or
library can also reference the core package directly:

```xml
<ItemGroup>
  <PackageReference Include="2dog.godotsharp" Version="$(TwoDogVersion)" />
</ItemGroup>
```

No DisableImplicitGodotSharpReferences or DisableImplicitGodotGeneratorReferences properties
are required. Normal Godot.NET.Sdk defaults work on the first restore and subsequent restores.
The package selects fork assemblies in place of compatible stock package assets and keeps
one fork source generator. Dependencies targeting a different Godot version still fail TDG001.

The core package carries a private editor binding fallback. It becomes a reference only when
Godot.NET.Sdk requests editor APIs (Debug/Editor), or a dependency requests GodotSharpEditor.
Plain .NET projects using editor APIs explicitly should reference 2dog.godotsharp.editor.
The separate editor package remains supported and must match the core version exactly.

A host dependency cannot change how a referenced game compiles. The CLI therefore adds the
managed references to existing games as well as new ones. Compatible stock dependencies in
legacy game and third-party library projects no longer prevent a host from building/publishing.

Resolved compile/copy and publish inputs are checked by content hash after package selection.
Stale manual HintPaths still fail, even when assembly versions match. Publish checks run before
ILLink rewrites assemblies and also cover publish --no-build.

The core package uses the fork's Release core API and Debug editor API. Native debug/editor
selection remains controlled by TwoDogVariant. Packaging requires a matching fork build in
TwoDogGodotSharpApiDir. The source generator runs automatically for Godot.NET.Sdk games; custom
Microsoft.NET.Sdk games can set TwoDogEnableGodotSourceGenerators=true and supply GodotProjectDir.

The package preserves fork trim annotations. It does not mark consumer assemblies as trimmable
or suppress IL2125; application and library reflection still requires its own trimming review.
