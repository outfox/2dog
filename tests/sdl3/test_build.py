"""build-godot.py's SDL3 platform: static archives and the external SDL tree, without compiling Godot."""
import argparse
import importlib.util
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    result = importlib.util.module_from_spec(spec)
    sys.modules[name] = result
    spec.loader.exec_module(result)
    return result


build = module("build_godot_sdl3_tests", REPO / "build-godot.py")
config = build.get_platform_config("sdl3", "x86_64")


def args(**overrides):
    """The sdl3 fields build_libgodot reads; overrides keep the defaults a real CLI run supplies."""
    return argparse.Namespace(**{
        "target": "template_debug", "dev_build": "no", "scu_build": "yes", "debug_symbols": "no",
        "cache_path": "", "precision": "single", "static": False,
        "sdl3_external_root": "", "sdl3_external_sources": "",
        **overrides,
    })


def commands(staged=None, **overrides):
    """The scons command lines build_libgodot would run. `staged` lists the artifact names SCons would produce;
    staging after the build copies them, so the fakes have to exist."""
    options = args(**overrides)
    with patch.object(build, "run_with_live_output") as run, tempfile.TemporaryDirectory() as scratch:
        old = Path.cwd()
        try:
            os.chdir(scratch)
            output = Path("godot/bin")
            output.mkdir(parents=True)
            for name in staged or []:
                (output / name).write_bytes(b"native")
            build.build_libgodot(options, config)
        finally:
            os.chdir(old)
    return [call.args[0] for call in run.call_args_list]


ARCHIVE = "libgodot.sdl3.template_debug.x86_64.a"
SHARED = "godot.sdl3.template_debug.x86_64.shared_library.dll"


class StaticArchiveTests(unittest.TestCase):
    def test_static_build_asks_for_an_archive_and_drops_the_shared_suffix(self):
        command = commands(staged=[ARCHIVE], static=True)[0]
        self.assertIn("library_type=static_library", command)
        self.assertFalse(any(flag.startswith("extra_suffix=") for flag in command))

    def test_default_build_stays_shared(self):
        command = commands(staged=[SHARED])[0]
        self.assertIn("library_type=shared_library", command)
        self.assertIn("extra_suffix=shared_library", command)

    def test_callers_without_the_static_option_still_build(self):
        # Programmatic callers predate --static; the attribute must not be required.
        legacy = argparse.Namespace(target="template_debug", dev_build="no", scu_build="yes",
                                    debug_symbols="no", cache_path="")
        with patch.object(build, "run_with_live_output") as run, tempfile.TemporaryDirectory() as scratch:
            old = Path.cwd()
            try:
                os.chdir(scratch)
                output = Path("godot/bin")
                output.mkdir(parents=True)
                (output / SHARED).write_bytes(b"native")
                build.build_libgodot(legacy, config)
            finally:
                os.chdir(old)
        self.assertIn("library_type=shared_library", run.call_args.args[0])


class ExternalTreeTests(unittest.TestCase):
    def test_external_tree_flags_reach_scons(self):
        with tempfile.TemporaryDirectory() as tree:
            (Path(tree) / "include").mkdir()
            sources = Path(tree) / "sources.txt"
            sources.write_text("src/a.c\nsrc/b.c\n")
            command = commands(staged=[SHARED], sdl3_external_root=tree,
                               sdl3_external_sources="@" + str(sources))[0]
        self.assertIn("sdl3_host=external", command)
        self.assertIn(f"sdl3_external_root={Path(tree).resolve()}", command)
        self.assertIn("sdl3_external_sources=src/a.c src/b.c", command)

    def test_relative_root_resolves_from_the_invocation_directory(self):
        # SCons runs in godot/, so an unresolved relative path would name a different tree.
        with tempfile.TemporaryDirectory() as tree, patch.object(build, "run_with_live_output") as run:
            (Path(tree) / "godot/bin").mkdir(parents=True)
            (Path(tree) / "godot/bin" / SHARED).write_bytes(b"native")
            old = Path.cwd()
            try:
                os.chdir(tree)
                build.build_libgodot(args(sdl3_external_root="sdl", sdl3_external_sources="src/a.c"), config)
            finally:
                os.chdir(old)
        self.assertIn(f"sdl3_external_root={Path(tree).resolve() / 'sdl'}", run.call_args.args[0])

    def test_sources_without_a_root_are_rejected(self):
        with patch.object(build, "run_with_live_output") as run:
            with self.assertRaises(ValueError):
                build.build_libgodot(args(sdl3_external_sources="src/a.c"), config)
            run.assert_not_called()

    def test_root_without_sources_is_rejected(self):
        with patch.object(build, "run_with_live_output") as run:
            with self.assertRaises(ValueError):
                build.build_libgodot(args(sdl3_external_root="somewhere"), config)
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
