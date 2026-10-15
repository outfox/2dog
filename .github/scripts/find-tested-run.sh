#!/usr/bin/env bash
# Finds a recent successful ci.yml run that already tested the content of <commit>: the same tree once root Markdown
# and AGENTS.md files are left out, with every given job family run and passed. Prints the run id and the commit it
# ran on; exits 1 when there is none. Needs GH_TOKEN and GITHUB_REPOSITORY.
#   find-tested-run.sh <commit-sha> <family>...    e.g. find-tested-run.sh "$SHA" Test "Desktop Smoke"
set -euo pipefail

commit=$1
shift
api="repos/$GITHUB_REPOSITORY"

# Longest first: a job belongs to the first family it matches, so "Test Rendering" is not counted as "Test".
families="[$(printf '%s\n' "$@" | awk '{ print length "\t" $0 }' | sort -rn | cut -f2- | sed 's/.*/"&"/' | paste -sd,)]"

# The recursive tree from the API, so candidate commits need no fetch. Unlike ci.yml's paths-ignore, docs/ counts:
# DocsDriftTests read its pages and sidebar.
content_key() {
  gh api "$api/git/trees/$1?recursive=1" --jq '
    if .truncated then error("tree listing truncated") else . end
    | .tree[] | select(.type != "tree" and (.path | test("^[^/]+\\.md$|(^|/)AGENTS\\.md$") | not))
    | "\(.mode) \(.type) \(.sha)\t\(.path)"' | sha256sum | cut -d' ' -f1
}

tree=$(gh api "$api/commits/$commit" --jq '.commit.tree.sha')
want=$(content_key "$tree")

mapfile -t runs < <(gh api "$api/actions/workflows/ci.yml/runs?status=success&per_page=40" \
  --jq '.workflow_runs[] | "\(.id) \(.head_sha) \(.head_commit.tree_id)"')

# A green run can hide skipped jobs: every family must have run, and every job in them passed.
verified() {
  local verdict
  verdict=$(gh api "$api/actions/runs/$1/jobs?per_page=100" --jq "$families as \$families | [.jobs[] | . as \$job
      | (\$families | map(. as \$f | select(\$job.name == \$f or (\$job.name | startswith(\$f + \" \")))) | first) as \$family
      | select(\$family != null) | {family: \$family, conclusion}] as \$gated
    | (\$gated | all(.conclusion == \"success\")) and ((\$gated | map(.family) | unique | length) == (\$families | length))" \
    2>/dev/null || echo false)
  [ "$verdict" = "true" ]
}

# Identical trees first (a merge of a tested, up-to-date branch); then the newest runs by filtered content.
for entry in "${runs[@]}"; do
  read -r run sha candidate <<<"$entry"
  [ "$candidate" = "$tree" ] && verified "$run" && { echo "$run $sha"; exit 0; }
done
for entry in "${runs[@]:0:15}"; do
  read -r run sha candidate <<<"$entry"
  [ "$candidate" != "$tree" ] && [ "$(content_key "$candidate" 2>/dev/null || true)" = "$want" ] && verified "$run" &&
    { echo "$run $sha"; exit 0; }
done
exit 1
