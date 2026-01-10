# What's 2dog?
Godot, but backwards!

> *To dog, or not to dog. Is that even a question?*

## Summary
**2dog** is a .NET/C# front-end for [Godot](https://github.com/godotengine/godot), meaning it can interact with the engine, but isn't bound by its limitations; imagine it like a dog that follows you everywhere, and that dog run circles around you.

Everything that Godot and GodotSharp can do, **2dog** does, but this dog knows a lot more tricks, on top.

## (massively WIP!)
Planned features are TRS transforms using the [**fenn**ecs](https://fennecs.net) entity-component system, and a new approach to scene and material definitions.

## Acknowledgements
Based on Ben Rog-Wilhelm's [zorbathut/libgodot_example](https://github.com/zorbathut/libgodot_example/tree/csharp). *You truly are the GOAT, even if looking more like a Diesel Horse of sorts in my mind !*


# How To Build & Run C#

Run `./build.py`, and on success, `dotnet run --project engine -r linux-x64`. Only tested on linux so far, the -r RID requirement will be lifted soon. :)

### Legacy Docs
<details>
** What This Is

Once Godot 4.6 is released, most of this won't be necessary.

It will take a while; it has to build the entire Godot engine twice, plus some more stuff.

Godot is started from C#, then the label text is updated from the same C#. There's no scripting in this project! It's all driven by the outer harness starting Godot itself.

Supports Windows and Linux.

(You can ignore the poetry file, it's just for me.)

** License

This is dual-licensed under the MIT License and the Unlicense. You're welcome to treat it as under public domain, if such a thing is legal. Go wild.

</details>

Peace out!
