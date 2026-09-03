---
title: 2dog pack
description: "Reference for 2dog pack list: list an exported .pck's contents by size, without an engine or project."
---

# `2dog pack`

File-format operations on Godot `.pck` bundles. Reads the pack directly: no
engine, no project.

```bash
2dog pack list <pck>
```

`list` prints the pack format, the Godot version and every entry, largest
first; `--json` puts the listing in the report. It answers "why is my pck
99 MiB?" and confirms an asset made it into a [web publish](/hosts/web).

```bash
2dog pack list MyGame.web/AppBundle/godot.pck
```

```text
MyGame.web/AppBundle/godot.pck: pack format v4 (Godot 4.7), 19 file(s), 0.0 MiB content
      21,582  .godot/imported/icon.svg-218a8f2b3041327d8a5756f3a245f83b.ctex
      11,593  icon.svg
       7,602  .godot/exported/133200997/export-3070c538c03ee49b7677ff960a3f5195-main.scn
         656  project.binary
         316  spinning_cube.gdc
...
```
