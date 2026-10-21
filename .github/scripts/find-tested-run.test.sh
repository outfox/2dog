#!/usr/bin/env bash
# Regression: builds can occupy the first 100 jobs; later failures must still block reuse.
set -euo pipefail
cd "$(dirname "$0")"
scratch=$(mktemp -d)
page1="$scratch/page1.json"
page2="$scratch/page2.json"
trap 'rm -f "$page1" "$page2"; rmdir "$scratch"' EXIT
api=repos/test/repository
families='["Test Rendering","WebGPU Desktop","Desktop Smoke","iOS Smoke","Test"]'
eval "$(sed -n '/^verified() {/,/^}/p' find-tested-run.sh)"

# Keep the actual jq predicate and emulate GitHub's paging boundary without network access.
gh() {
  local paginate=false slurp=false query
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --paginate) paginate=true ;;
      --slurp) slurp=true ;;
      --jq) shift; query=$1 ;;
    esac
    shift
  done
  if $slurp && [ -n "${query:-}" ]; then echo '--slurp does not support --jq' >&2; return 1; fi
  if $paginate && $slurp; then jq -s '.' "$page1" "$page2";
  else jq '.' "$page1"; fi
}

jq -n '{jobs: [range(100) | {name: "Build Natives", conclusion: "success"}]}' > "$page1"
jq -n '{jobs: ["Test (linux)", "Test Rendering (win)", "Desktop Smoke (linux)", "WebGPU Desktop (linux)", "iOS Smoke (single)"]
  | map({name: ., conclusion: "success"})}' > "$page2"
verified 1 || { echo 'Complete paginated run was not reusable'; exit 1; }
for conclusion in failure skipped cancelled; do
  jq --arg value "$conclusion" '.jobs[-1].conclusion = $value' "$page2" > "$page1"
  mv "$page1" "$page2"
  printf '{"jobs":[]}' > "$page1"
  if verified 1; then echo "Reused a $conclusion iOS check"; exit 1; fi
done
jq 'del(.jobs[-1])' "$page2" > "$page1"
printf '{"jobs":[]}' > "$page2"
if verified 1; then echo 'Reused a run missing iOS checks'; exit 1; fi
echo 'PASS: CI reuse requires all job pages, families and successful conclusions'
