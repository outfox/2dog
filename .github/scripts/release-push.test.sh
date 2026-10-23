#!/usr/bin/env bash
set -euo pipefail
script="$(cd "$(dirname "$0")" && pwd)/release-push.sh"
scratch=$(mktemp -d)
trap 'rm -f "$scratch/Directory.Build.props"; rmdir "$scratch"' EXIT
cd "$scratch"
printf '<GodotVersion>4.7.2</GodotVersion>\n<TwoDogRevision>135</TwoDogRevision>\n' > Directory.Build.props

git() {
  case "$1" in
    ls-remote) printf '%s' "$REMOTE"; return "${REMOTE_EXIT:-0}" ;;
    rev-parse) echo current-commit ;;
    *) echo "Unexpected git command: $*" >&2; return 1 ;;
  esac
}
export -f git
export REMOTE
tag=refs/tags/v4.7.2.135

for REMOTE in '' "$(printf 'other-commit\t%s\n' "$tag")"; do
  if bash "$script"; then echo 'Delegated without a matching release commit'; exit 1; fi
done
REMOTE=$(printf 'current-commit\t%s\n' "$tag")
test "$(bash "$script")" = v4.7.2.135
REMOTE=$(printf 'tag-object\t%s\ncurrent-commit\t%s^{}\n' "$tag" "$tag")
test "$(bash "$script")" = v4.7.2.135
# A tag object is never the commit: the peeled value takes precedence.
REMOTE=$(printf 'current-commit\t%s\nother-commit\t%s^{}\n' "$tag" "$tag")
if bash "$script"; then echo 'Ignored the annotated tag commit'; exit 1; fi
export REMOTE_EXIT=1
if bash "$script"; then echo 'Delegated after a failed remote lookup'; exit 1; fi
unset REMOTE_EXIT
printf '<GodotVersion>broken</GodotVersion>\n<TwoDogRevision>135</TwoDogRevision>\n' > Directory.Build.props
if bash "$script"; then echo 'Accepted malformed release properties'; exit 1; fi
echo 'PASS: branch delegation requires the exact remote release tag commit'
