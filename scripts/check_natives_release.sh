#!/usr/bin/env bash
# Exits 0 when the natives release <tag> exists with the assets added after the original layout: releases created
# before the Android payloads joined build-natives.yml lack them and must be rebuilt rather than reused.
set -euo pipefail
tag="$1"
if ! assets=$(gh release view "$tag" --repo "$GITHUB_REPOSITORY" --json assets -q '.assets[].name' 2>/dev/null); then
  echo "Release $tag does not exist"
  exit 1
fi
missing=""
for asset in godot-android-java.zip \
    libgodot-android-arm64-template_debug.zip libgodot-android-arm64-template_release.zip \
    libgodot-android-x64-template_debug.zip libgodot-android-x64-template_release.zip; do
  grep -qx "$asset" <<< "$assets" || missing="$missing $asset"
done
if [ -n "$missing" ]; then
  echo "Release $tag lacks:$missing"
  exit 1
fi
echo "Release $tag is complete"
