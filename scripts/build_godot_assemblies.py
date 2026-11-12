"""Run the fork's assembly builder with a precision-specific GodotTools output root."""
import importlib.util
from pathlib import Path
import sys


def main():
    source = Path(__file__).resolve().parents[1] / "godot/modules/mono/build_scripts/build_assemblies.py"
    spec = importlib.util.spec_from_file_location("godot_build_assemblies", source)
    builder = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = builder
    spec.loader.exec_module(builder)
    output = Path(sys.argv[sys.argv.index("--godot-output-dir") + 1]).resolve() / "GodotSharp"
    run_msbuild = builder.run_msbuild

    def isolated_msbuild(tools, sln, chdir_to, msbuild_args=None):
        # GodotTools hardcodes this root; the SDK copy target concatenates Tools/nupkgs without a separator.
        return run_msbuild(tools, sln, chdir_to,
                           [*(msbuild_args or []), f"/p:GodotOutputDataDir={output.as_posix()}/"])

    builder.run_msbuild = isolated_msbuild
    builder.main()


if __name__ == "__main__":
    main()
