# Workflows and CI scripts

These rules add to the root `AGENTS.md` for `.github/workflows/` and `.github/scripts/`.

## Workflows

- `ci.yml` reuses `godot-<submodule-hash>` native releases or builds missing natives through
  `build-natives.yml`, then packs, tests three OSes in Debug/Release/Editor and runs smoke jobs. `v*` tags
  also deploy to NuGet. Pushes that only touch `docs/**`, root Markdown or `AGENTS.md` files skip it.
- Each OS/configuration pair is its own test job in a fresh workspace, so no configuration relies on files
  another one built. Do not merge configurations back into one job: together they exceed the job timeout.
- `release.yml` bumps `TwoDogRevision`, tags `v<TwoDogVersion>` and dispatches `ci.yml` on the tag, because
  tags pushed with `GITHUB_TOKEN` start no workflows. `android.yml` runs Android build and package tests;
  `docs.yml` builds the documentation site.
- Keep the `github.repository == 'outfox/2dog'` gate on jobs that build, test or release.
- Downstream jobs must tolerate skipped `build-natives` with explicit
  `!cancelled() && needs.<job>.result == 'success'` gates; implicit `success()` skips them transitively.
- New Linux jobs run on `blacksmith-4vcpu-ubuntu-2404`. Do not change an existing job's runner size; the
  maintainer sizes runners from usage stats. Matrix jobs keep `${{ matrix.runner }}`.

## Scripts

- `test-with-watchdog.sh <Config> [idle-seconds] [budget-seconds]` runs `dotnet test` under a hang watchdog
  that dumps the test processes. It sets `TWODOG_GODOT_LOG_DIR` to `twodog.tests/TestResults/godot-<Config>`,
  so failed runs upload Godot's native logs with the test results.
- After changing the web or Blazor smoke scripts, run `node --test .github/scripts/*.test.mjs`.

## Checking runs

- A green run can hide skipped jobs: confirm that the test jobs ran, e.g. with `gh run view <run-id>`.
