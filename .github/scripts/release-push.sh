#!/usr/bin/env bash
# An atomic release-branch + release-tag push delegates validation and deployment to the tag run.
set -euo pipefail

version=$(sed -n 's:.*<GodotVersion>\(.*\)</GodotVersion>.*:\1:p' Directory.Build.props)
revision=$(sed -n 's:.*<TwoDogRevision>\(.*\)</TwoDogRevision>.*:\1:p' Directory.Build.props)
label=$(sed -n 's:.*<TwoDogPrerelease>\(.*\)</TwoDogPrerelease>.*:\1:p' Directory.Build.props)
[[ "$revision" =~ ^[0-9]+$ ]] || exit 1
# 2dog versions use Godot's numbers only: GodotVersion 4.8.0-beta.2 releases v4.8.0.<revision>[-label].
[[ "$version" =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-[0-9A-Za-z.]+)?$ ]] || exit 1
tag="refs/tags/v${BASH_REMATCH[1]}.$revision${label:+-$label}"
# Prefer the peeled commit for annotated/signed tags; lightweight tags already hold a commit.
remote=$(git ls-remote origin "$tag" "$tag^{}")
commit=$(awk -v tag="$tag" '$2 == tag { direct=$1 } $2 == tag "^{}" { peeled=$1 }
  END { print (peeled != "" ? peeled : direct) }' <<<"$remote")
[ "$commit" = "$(git rev-parse HEAD)" ] || exit 1
echo "${tag#refs/tags/}"
