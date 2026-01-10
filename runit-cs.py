#!/usr/bin/env python3

import subprocess
import os
import sys
import platform
import shutil


dev_build = "yes"
debug_symbols = "yes"
scu_build = "yes"


# Platform-specific settings
# TODO: Multi-Target-Support (or: we wait till godot provides a libgodot distro, then this file would be almost entirely unnecessary.
is_windows = platform.system() == "Windows"

if is_windows:
    godot_platform = "windows"
    godot_exe = os.path.abspath("godot/bin/godot.linuxbsd.editor.dev.x86_64.executable.mono")
    engine_exe = "engine/bin/Debug/net10.0/engine.exe"
    lib_path_var = "PATH"
    path_separator = ";"
else:
    godot_platform = "linuxbsd"
    godot_exe = "./bin/godot.linuxbsd.editor.dev.x86_64.executable.mono"
    engine_exe = "./engine/bin/Debug/net10.0/engine"
    lib_path_var = "LD_LIBRARY_PATH"
    path_separator = ":"


#print("Initializing Git submodules...")
#subprocess.run(["git", "submodule", "init"], check=True)

if "--no-build" not in sys.argv:
    # Build both dev+debug and release versions of the lib.
    print("Building libgodot...")
    for target in ["template_release", "editor"]:
        print(f"Building Godot shared library with Mono support... target={target}")
        subprocess.run(["scons", f"target={target}", "module_mono_enabled=yes", "library_type=shared_library", f"extra_suffix=shared_library",
                        f"dev_build={dev_build}", f"scu_build={scu_build}",
                        f"debug_symbols={debug_symbols}", f"separate_debug_symbols={debug_symbols}"],
        cwd="godot", check=True)


    print("Building Godot executable with Mono support...")
    # extra_suffix is just for compilation optimization, otherwise the binary and libgodot step on each other's feet and cause massively inflated iterative build times
    # scu_build is just to make the build faster
    subprocess.run(["scons", f"target=editor", "module_mono_enabled=yes", "extra_suffix=executable",
                    f"dev_build={dev_build}", f"scu_build={scu_build}",
                    f"debug_symbols=true", f"separate_debug_symbols=true"],
                   cwd="godot", check=True)


if "--no-glue" not in sys.argv:
    print("Making NuGet packages directory...")
    os.makedirs("godot/bin/GodotSharp/Tools/nupkgs", exist_ok=True)

    print("Generating game UID cache...")
    subprocess.run([godot_exe, "--path", "../project", "--import", "--headless"], cwd="godot", check=True)


    print("Generating Mono glue files...")
    subprocess.run([godot_exe, "--headless", "--generate-mono-glue", "./modules/mono/glue"], cwd="godot", check=True)


    print("Building C# assemblies and NuGet packages...")
    subprocess.run([
            "python",
            "./modules/mono/build_scripts/build_assemblies.py",
            "--godot-platform", godot_platform,
            "--godot-output-dir", "./bin",
            "--no-deprecated"
        ], cwd="godot", check=True)


print("Building engine...")
subprocess.run(["dotnet", "restore"], cwd="engine", check=True)
subprocess.run(["dotnet", "build"], cwd="engine", check=True)


# Check if --no-run parameter was passed; useful for debugging
if "--no-run" in sys.argv:
    print("Build complete (skipping driver run)!")
    exit(0)

print("Restoring .NET packages...")
subprocess.run(["dotnet", "restore"], cwd="game", check=True)
subprocess.run(["dotnet", "restore"], cwd="project", check=True)

print("Running engine...")
env = os.environ.copy()
lib_path = os.path.abspath("godot/bin")
env[lib_path_var] = lib_path + path_separator + env.get(lib_path_var, "")
subprocess.run([engine_exe], env=env, check=True)

print("Done!")
