#!/usr/bin/env bash
set -euo pipefail
script="$(cd "$(dirname "$0")" && pwd)/stage-ci-inputs.sh"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
for precision in single double; do
  root="$scratch/$precision"
  mkdir -p "$root/.ci/downloads/godot-bin-$precision/bin" "$root/.ci/downloads/godot-bin-$precision/modules" \
    "$root/.ci/downloads/nuget-packages-$precision" "$root/godot"
  printf '%s' "$precision natives" > "$root/.ci/downloads/godot-bin-$precision/bin/native"
  mkdir -p "$root/godot/bin/GodotSharp/Tools/nupkgs" "$root/packages"
  mkdir -p "$root/.ci/downloads/godot-bin-$precision/bin/GodotSharp/Tools/nupkgs"
  printf 'downloaded SDK' > "$root/.ci/downloads/godot-bin-$precision/bin/GodotSharp/Tools/nupkgs/fresh.nupkg"
  printf 'existing SDK' > "$root/godot/bin/GodotSharp/Tools/nupkgs/fixture.nupkg"
  printf 'stale native' > "$root/godot/bin/native"
  printf 'stale package' > "$root/packages/test.nupkg"
  printf '%s' "$precision package" > "$root/.ci/downloads/nuget-packages-$precision/test.nupkg"
  (cd "$root" && bash "$script" "$precision")
  test "$(cat "$root/godot/bin/native")" = "$precision natives"
  test -d "$root/godot/modules"
  test "$(cat "$root/godot/bin/GodotSharp/Tools/nupkgs/fixture.nupkg")" = 'existing SDK'
  test "$(cat "$root/godot/bin/GodotSharp/Tools/nupkgs/fresh.nupkg")" = 'downloaded SDK'
  test "$(cat "$root/packages/test.nupkg")" = "$precision package"
done
for missing in godot-bin nuget-packages; do
  root="$scratch/missing-$missing"
  mkdir -p "$root"
  if [ "$missing" != godot-bin ]; then mkdir -p "$root/.ci/downloads/godot-bin-single/bin"; fi
  if [ "$missing" != nuget-packages ]; then
    mkdir -p "$root/.ci/downloads/nuget-packages-single"
    printf 'package' > "$root/.ci/downloads/nuget-packages-single/test.nupkg"
  fi
  if (cd "$root" && bash "$script" single); then echo "Accepted missing $missing inputs"; exit 1; fi
  test ! -e "$root/godot"
  test ! -e "$root/packages"
done
root="$scratch/web-inputs"
mkdir -p "$root/.ci/downloads/godot-bin-single/bin" "$root/.ci/downloads/runtime-packages-single"
printf 'core package' > "$root/.ci/downloads/runtime-packages-single/core.nupkg"
if (cd "$root" && WEB_INPUTS=true PACKAGE_INPUT=runtime-packages bash "$script" single); then
  echo 'Accepted missing Web inputs'; exit 1
fi
test ! -e "$root/godot"
mkdir -p "$root/.ci/downloads/godot-web-single/godot/bin/web" "$root/.ci/downloads/godot-web-single/packages"
printf 'web payload' > "$root/.ci/downloads/godot-web-single/godot/bin/web/native"
printf 'web package' > "$root/.ci/downloads/godot-web-single/packages/browser.nupkg"
(cd "$root" && WEB_INPUTS=true PACKAGE_INPUT=runtime-packages bash "$script" single)
test "$(cat "$root/godot/bin/web/native")" = 'web payload'
test "$(cat "$root/packages/core.nupkg")" = 'core package'
test "$(cat "$root/packages/browser.nupkg")" = 'web package'
echo 'PASS: concurrent artifact staging preserves both layouts and rejects incomplete downloads'
