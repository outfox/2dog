#!/usr/bin/env bash
# Move the concurrent downloads into their usual locations on the same filesystem, without copying their payloads.
set -euo pipefail
precision=$1
inputs=.ci/downloads
package_input=${PACKAGE_INPUT:-nuget-packages}
# A pattern download may succeed with only one artifact; require both before moving either.
test -d "$inputs/godot-bin-$precision/bin"
test -d "$inputs/$package_input-$precision"
if [ "${WEB_INPUTS:-false}" = true ]; then
  test -d "$inputs/godot-web-$precision/godot/bin"
  test -d "$inputs/godot-web-$precision/packages"
fi
# Source checks may already have created nested directories; merge without copying large native payloads.
shopt -s nullglob dotglob
move_inputs() {
  local source=$1 destination=$2 entry
  mkdir -p "$destination"
  for entry in "$source/"*; do
    if [ -d "$entry" ] && [ ! -L "$entry" ] && [ -d "$destination/${entry##*/}" ]; then
      move_inputs "$entry" "$destination/${entry##*/}"
    else
      mv -f "$entry" "$destination/"
    fi
  done
}
move_inputs "$inputs/godot-bin-$precision" godot
move_inputs "$inputs/$package_input-$precision" packages
if [ "${WEB_INPUTS:-false}" = true ]; then
  move_inputs "$inputs/godot-web-$precision/godot" godot
  move_inputs "$inputs/godot-web-$precision/packages" packages
fi
