# What's 2dog?
Godot, but backwards!

> *To dog, or not to dog. Is that even a question?*

## Summary
**2dog** is a .NET/C# front-end for [Godot](https://github.com/godotengine/godot), meaning it can interact with the engine, but isn't bound by its limitations; imagine it like a dog that follows you everywhere, and that dog run circles around you, jumps into your bed, and chases squirels.

Everything that Godot and GodotSharp can do, **2dog** does, but this dog knows a lot more tricks, on top.

## (massively WIP!)
Planned features are TRS transforms using the [**fenn**ecs](https://fennecs.net) entity-component system, and a new approach to scene and material definitions. When Godot 4.6 is released, the local build portion of this will likely not be necessary.

## Acknowledgements
Based on Ben Rog-Wilhelm's [zorbathut/libgodot_example](https://github.com/zorbathut/libgodot_example/tree/csharp). *You truly are the GOAT, or maybe [DIESEL HORSE](https://diesel.horse)!*


# How To Build & Run C#
Init sumodules via `git submodule init` and `git submodule update` as needed. This checks out a lightly modified fork of https://github.com/godotengine/godot .

Run `uv run build.py`, and on success, `dotnet run --project engine`. Only tested on linux so far.)



#### *No squirrels were harmed in the making of this README.*
