#!/usr/bin/env bash
# Move the concurrent downloads into their usual locations on the same filesystem, without copying their payloads.
set -euo pipefail
precision=$1
inputs=.ci/downloads
# A pattern download may succeed with only one artifact; require both before moving either.
test -d "$inputs/godot-bin-$precision/bin"
test -d "$inputs/nuget-packages-$precision"
mkdir -p godot packages
mv "$inputs/godot-bin-$precision/"* godot/
mv "$inputs/nuget-packages-$precision/"* packages/
