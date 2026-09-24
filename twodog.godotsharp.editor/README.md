# 2dog.godotsharp.editor

The 2dog fork's GodotSharpEditor.dll, without native engine packages.
Depends on the exact matching 2dog.godotsharp version. Use the same version as 2dog.engine.

Reference this package in plain .NET projects that use editor APIs. Godot.NET.Sdk games also
receive editor bindings automatically in Debug/Editor through the core package's fallback.
No implicit-reference opt-out properties are required. The core package validates both assemblies
before compilation and publish trimming.
