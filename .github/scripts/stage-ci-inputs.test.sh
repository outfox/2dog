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
  printf '%s' "$precision package" > "$root/.ci/downloads/nuget-packages-$precision/test.nupkg"
  (cd "$root" && bash "$script" "$precision")
  test "$(cat "$root/godot/bin/native")" = "$precision natives"
  test -d "$root/godot/modules"
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
echo 'PASS: concurrent artifact staging preserves both layouts and rejects incomplete downloads'
