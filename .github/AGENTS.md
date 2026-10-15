# Workflows and CI scripts

These rules add to the root `AGENTS.md` for `.github/workflows/` and `.github/scripts/`.

## Workflows

- `ci.yml` reuses `godot-<submodule-hash>` and `godot-<submodule-hash>-double` native releases or builds missing natives through
  `build-natives.yml`, then packs and tests both precisions on three OSes in Debug/Release/Editor and runs smoke jobs. `v*` tags
  also deploy to NuGet. Pushes that only touch `docs/**`, root Markdown or `AGENTS.md` files skip it.
- Each OS/configuration pair is its own test job in a fresh workspace, so no configuration relies on files
  another one built. Do not merge configurations back into one job: together they exceed the job timeout.
- The test matrix is priced by runner: macOS minutes cost 5x Windows and 10x Linux. Linux runs every
  configuration/precision pair; Windows and macOS cover each configuration and each precision at least once.
  Configuration-independent suites (CLI tool tests, MTP contracts) run in the Debug jobs, REPL coverage in
  Windows Debug single. Add new suites the same way rather than to every cell.
- `release.yml` bumps `TwoDogRevision`, tags `v<TwoDogVersion>` and dispatches `ci.yml` on the tag, because
  tags pushed with `GITHUB_TOKEN` start no workflows. When the tag's parent passed CI and the tag only bumps
  the revision, the tag run skips the test, desktop and Android jobs. `android.yml` runs Android build and
  package tests; `docs.yml` builds the documentation site.
- A branch push whose content already passed every job family in a recent successful run skips natives, pack,
  test and smoke jobs; the run summary links the verifying run. Content means the tree without root Markdown
  and `AGENTS.md` files, so merging a tested, up-to-date branch costs one short job. `docs/` counts, because
  tool tests read it. `scripts/find-tested-run.sh` does the matching for this rule and the tag rule; keep its
  family list in step with the job names, and never ignore files a test reads. Manual dispatches always run in full.
- Keep the `github.repository == 'outfox/2dog'` gate on jobs that build, test or release.
- Downstream jobs must tolerate skipped `build-natives` with explicit
  `!cancelled() && needs.<job>.result == 'success'` gates; implicit `success()` skips them transitively.
- New Linux jobs run on `blacksmith-4vcpu-ubuntu-2404`. Do not change an existing job's runner size; the
  maintainer sizes runners from usage stats. Matrix jobs keep `${{ matrix.runner }}`.
- Blacksmith runs the jobs that set the workflow's duration: pack, the Debug test jobs, web and Android
  smoke. Jobs that finish well before them (Release/Editor tests, macOS Release excepted, desktop and tool
  smoke) run on GitHub-hosted runners, which are free for this public repository but take about twice as long.

## Scripts

- `test-with-watchdog.sh <Config> [idle-seconds] [budget-seconds]` runs `dotnet test` under a hang watchdog
  that dumps the test processes. It sets `TWODOG_GODOT_LOG_DIR` to `twodog.tests/TestResults/godot-<Config>`,
  so failed runs upload Godot's native logs with the test results. `TWODOG_TEST_FILTER` adds a `--filter`.
- After changing the web or Blazor smoke scripts, run `node --test .github/scripts/*.test.mjs`.

## Checking runs

- A green run can hide skipped jobs: confirm that the test jobs ran, e.g. with `gh run view <run-id>`.
