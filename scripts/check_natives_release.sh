#!/usr/bin/env bash
# Exits 0 when the natives release <tag> holds every asset build-natives.yml produces and ci.yml's pack job stages.
# An incomplete draft can be repaired; incomplete published identities require intervention.
# Keep this list in step with the build-natives.yml artifacts.
set -euo pipefail
tag="$1"
if [ "$(gh release view "$tag" --repo "$GITHUB_REPOSITORY" --json isDraft -q .isDraft 2>/dev/null)" != false ]; then
  echo "Release $tag is missing or still a draft"
  exit 1
fi
if ! assets=$(gh release view "$tag" --repo "$GITHUB_REPOSITORY" --json assets -q '.assets[].name' 2>/dev/null); then
  echo "Release $tag does not exist"
  exit 1
fi
expected=(native-build.json godotsharp.zip godotsharp-nupkgs.zip godot-source-generators.zip godot-editor-linux-x64.zip
  godot-android-java.zip
  libgodot-browser-wasm-template_debug.zip libgodot-browser-wasm-template_release.zip
  libgodot-browser-wasm-threads-template_debug.zip libgodot-browser-wasm-threads-template_release.zip)
for rid in linux-x64 win-x64 osx-arm64; do
  for target in template_debug template_release editor; do
    expected+=("libgodot-$rid-$target.zip")
  done
done
for rid in android-arm64 android-x64 ios-arm64 iossimulator-arm64; do
  for target in template_debug template_release; do
    expected+=("libgodot-$rid-$target.zip")
  done
done
missing=""
for asset in "${expected[@]}"; do
  grep -qx "$asset" <<< "$assets" || missing="$missing $asset"
done
if [ -n "$missing" ]; then
  echo "Release $tag lacks:$missing"
  exit 1
fi
echo "Release $tag is complete"
