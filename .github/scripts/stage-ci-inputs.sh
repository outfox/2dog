#!/usr/bin/env bash
# Move the concurrent downloads into their usual locations on the same filesystem, without copying their payloads.
set -euo pipefail
precision=$1
inputs=.ci/downloads
# A pattern download may succeed with only one artifact; require both before moving either.
test -d "$inputs/godot-bin-$precision/bin"
test -d "$inputs/nuget-packages-$precision"
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
move_inputs "$inputs/nuget-packages-$precision" packages
