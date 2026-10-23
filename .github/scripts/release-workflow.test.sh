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
git() {
  case "$1" in
    ls-remote) return 2 ;;
    diff) return 0 ;;
    *) echo "Unexpected git command: $*" >&2; return 1 ;;
  esac
}
export -f git
export GITHUB_OUTPUT="$scratch/output" INPUT_NATIVE INPUT_REVISION
for INPUT_NATIVE in false true; do
  printf '<GodotVersion>4.7.2</GodotVersion>\n<TwoDogRevision>134</TwoDogRevision>\n<NativesRevision>46</NativesRevision>\n' > Directory.Build.props
  : > "$GITHUB_OUTPUT"
  INPUT_REVISION=140
  bash -euo pipefail <<<"$compute"
  grep -qx 'tag=v4.7.2.140' "$GITHUB_OUTPUT"
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
: > "$GITHUB_OUTPUT"
bash -euo pipefail <<<"$compute"
grep -qx 'new_rev=141' "$GITHUB_OUTPUT"
for INPUT_REVISION in 140 invalid; do
  if bash -euo pipefail <<<"$compute"; then echo 'Accepted an invalid revision'; exit 1; fi
done
echo 'PASS: manual fallback bumps natives only when requested and validates revisions'
