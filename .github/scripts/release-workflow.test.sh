#!/usr/bin/env bash
# Exercise the fallback workflow's real version and edit steps without GitHub writes.
set -euo pipefail
workflow="$(cd "$(dirname "$0")/../workflows" && pwd)/release.yml"
scratch=$(mktemp -d)
trap 'rm -f "$scratch/Directory.Build.props" "$scratch/output"; rmdir "$scratch"' EXIT
cd "$scratch"
step() {
  awk -v name="$1" '
    $0 == "      - name: " name { found=1; next }
    found && /^      - name:/ { exit }
    found && /^        run: \|/ { run=1; next }
    run { sub(/^          /, ""); print }
  ' "$workflow"
}
compute=$(step 'Compute new version')
edit=$(step 'Bump revisions')
# Release tags on origin, one per line; a peeled annotated tag repeats its name with ^{}.
TAGS=''
git() {
  case "$1" in
    ls-remote) [ -z "$TAGS" ] || printf 'sha\trefs/tags/%s\n' $TAGS ;;
    diff) return 0 ;;
    *) echo "Unexpected git command: $*" >&2; return 1 ;;
  esac
}
export -f git
export GITHUB_OUTPUT="$scratch/output" GITHUB_REF=refs/heads/4.7 INPUT_NATIVE INPUT_REVISION TAGS
props() { printf '<GodotVersion>%s</GodotVersion>\n<TwoDogRevision>134</TwoDogRevision>\n<NativesRevision>46</NativesRevision>\n' "$1" > Directory.Build.props; }
for INPUT_NATIVE in false true; do
  props 4.7.2
  : > "$GITHUB_OUTPUT"
  INPUT_REVISION=140
  bash -euo pipefail <<<"$compute"
  grep -qx 'tag=v4.7.2.140' "$GITHUB_OUTPUT"
  grep -qx 'branch=4.7' "$GITHUB_OUTPUT"
  native=46
  [ "$INPUT_NATIVE" != true ] || native=47
  grep -qx "new_native_rev=$native" "$GITHUB_OUTPUT"
  update="$edit"
  while IFS='=' read -r key value; do
    placeholder='${{ steps.version.outputs.'"$key"' }}'
    update=${update//"$placeholder"/"$value"}
  done < "$GITHUB_OUTPUT"
  bash -euo pipefail <<<"$update"
  grep -qx '<TwoDogRevision>140</TwoDogRevision>' Directory.Build.props
  grep -qx "<NativesRevision>$native</NativesRevision>" Directory.Build.props
done
INPUT_NATIVE=false
INPUT_REVISION=''
props 4.7.2
: > "$GITHUB_OUTPUT"
bash -euo pipefail <<<"$compute"
grep -qx 'new_rev=135' "$GITHUB_OUTPUT"
# One sequence across lines: a newer revision on another line's tag moves this release past it.
TAGS='v4.7.2.134 v4.8.0-beta.1.150 v4.8.0-beta.1.150^{} v4.6.2.31'
: > "$GITHUB_OUTPUT"
bash -euo pipefail <<<"$compute"
grep -qx 'tag=v4.7.2.151' "$GITHUB_OUTPUT"
props 4.8.0-beta.1
GITHUB_REF=refs/heads/4.8
: > "$GITHUB_OUTPUT"
bash -euo pipefail <<<"$compute"
grep -qx 'tag=v4.8.0-beta.1.151' "$GITHUB_OUTPUT"
grep -qx 'branch=4.8' "$GITHUB_OUTPUT"
reject() {
  # Capture expected errors so their workflow commands do not create real CI annotations.
  if output=$(bash -euo pipefail <<<"$compute" 2>&1); then echo "Accepted $1"; exit 1; fi
  printf '%s\n' "$output" | sed 's/::error::/Expected rejection: /g'
}
for INPUT_REVISION in 150 invalid; do reject "revision $INPUT_REVISION"; done
INPUT_REVISION=''
GITHUB_REF=refs/heads/4.7
reject 'a prerelease from another line branch'
GITHUB_REF=refs/heads/4.8
for version in 4.8.0-beta 4.8.0-dev.3; do props "$version"; reject "GodotVersion $version"; done
echo 'PASS: manual fallback continues the revision sequence across lines and releases only from the line branch'
