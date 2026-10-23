#!/usr/bin/env bash
# An atomic main + release-tag push delegates validation and deployment to the tag run.
set -euo pipefail

version=$(sed -n 's:.*<GodotVersion>\(.*\)</GodotVersion>.*:\1:p' Directory.Build.props)
revision=$(sed -n 's:.*<TwoDogRevision>\(.*\)</TwoDogRevision>.*:\1:p' Directory.Build.props)
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$revision" =~ ^[0-9]+$ ]] || exit 1
tag="refs/tags/v$version.$revision"
# Prefer the peeled commit for annotated/signed tags; lightweight tags already hold a commit.
remote=$(git ls-remote origin "$tag" "$tag^{}")
commit=$(awk -v tag="$tag" '$2 == tag { direct=$1 } $2 == tag "^{}" { peeled=$1 }
  END { print (peeled != "" ? peeled : direct) }' <<<"$remote")
[ "$commit" = "$(git rev-parse HEAD)" ] || exit 1
echo "${tag#refs/tags/}"
