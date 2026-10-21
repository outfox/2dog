#!/usr/bin/env bash
# Keep upload concurrency bounded and fail deployment if any package cannot be pushed.
set -euo pipefail
find "${1:-packages}" -maxdepth 1 -type f -name '*.nupkg' -print0 |
  xargs -0 -r -P 4 -I '{}' bash -c '
    echo "Pushing $1..."
    dotnet nuget push "$1" --source https://api.nuget.org/v3/index.json \
      --api-key "$NUGET_API_KEY" --skip-duplicate
  ' _ '{}'
