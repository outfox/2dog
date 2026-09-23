# 2dog.godotsharp.editor

The 2dog fork's GodotSharpEditor.dll, without native engine packages.
Depends on the exact matching 2dog.godotsharp version. Use the same version as 2dog.engine.

Reference this package in projects that use editor APIs. Godot.NET.Sdk projects must disable
implicit GodotSharp and source-generator references, as described in the 2dog.godotsharp README.
The core package validates both assemblies before compilation and publish trimming.
