#!/usr/bin/env bash
# Keep upload concurrency bounded and fail deployment if any package cannot be pushed.
# Usage: push-nuget.sh [dir] [feed index URL]
set -euo pipefail
export NUGET_SOURCE="${2:-https://api.nuget.org/v3/index.json}"
# NuGet's first run migrates its folders; parallel first runs race on that (NuGet-Migrations, ENOENT).
dotnet nuget locals all --list > /dev/null
find "${1:-packages}" -maxdepth 1 -type f -name '*.nupkg' -print0 |
  xargs -0 -r -P 4 -I '{}' bash -c '
    echo "Pushing $1 to $NUGET_SOURCE..."
    dotnet nuget push "$1" --source "$NUGET_SOURCE" --api-key "$NUGET_API_KEY" --skip-duplicate
  ' _ '{}'
