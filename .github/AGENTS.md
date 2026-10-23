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
  CLI tests marked `Precision=Independent` run once per OS in Debug single; unmarked precision/native tests
  stay in both precisions. Windows Debug single excludes `FullyQualifiedName~Repl` from its ordinary pass
  and runs that exact selection under the 95% coverage gate. Keep native-state collections serialized.
- `test-contracts` runs independently after pack, preserving Debug adapter coverage in both precisions on
  Linux/Windows and single on macOS. Keep `Test Contracts` in both reuse family lists and the deployment gate.
  Preserve the harness's cancellation, cleanup and source-isolation assertions and subprocess deadlines.
- Desktop publish and editor lifecycle checks use the normal renderer settings. `webgpu-desktop` alone
  runs the native WebGPU pixels, restart and shader-contract checks, once per supported OS in single precision.
  Its Linux host also verifies the engine's ignored-X11-event fallback, which prevents libX11 cookie cleanup crashes.
  Keep it in both tested-content/revision-bump family lists and the deployment gate.
- The native build runs the real C++ shader compiler regressions once, against the single-precision,
  non-threaded Web debug archive. Source-level WebGPU checks run where the submodule source is checked out.
  Precision-independent package and browser-script checks run once rather than in every precision/AOT cell.
- `web-smoke` runs Web, WebXR, threaded Web and Blazor in separate flavor/precision/AOT matrix cells;
  its single `checks` cell runs MSBuild contracts and browser diagnostics. Portable showcase cells select
  Compatibility before publishing because double precision and threads do not support WebGPU. Keep
  WebGPU image coverage and single-precision Blazor WebGPU restart coverage enabled independently.
- `webgpu-browser-build` publishes the single-precision image-test bundle after pack; `webgpu-browser`
  consumes it without waiting for Web Smoke. Both belong to the `WebGPU Browser` tested-content family;
  the browser job gates deployment and requires the bundle job to succeed.
- Android Smoke uses one APK per precision/variant matrix cell. `android-prepare` warms the SDK and a clean
  emulator snapshot in parallel with packing; consumers mount SDK/snapshot disks with `commit: false` and
  use `-no-snapshot-save`. Keep both jobs in the `Android Smoke` family; the matrix gates deployment.
  Stop the ADB server in an always-run step before sticky disk teardown; its executable keeps the SDK disk busy.
  Pack fills a scoped Android input disk and uploads the same content as a fallback. Consumers must verify
  its run/commit/precision before use; reset mismatched inputs before downloading the matching artifact.
  Its native hashes come from the original build outputs; check packaged libraries against those hashes
  before using them as the APK inspection reference, so a packaging mistake cannot validate itself.
  Never persist bin/obj or test-modified emulator state. Clear mutable 2dog/fork SDK packages from the
  NuGet disk before restore, preserving third-party dependencies. Concurrent cache commits may lose cache
  additions, so a miss must remain a correct cold build.
- NuGet publishing uses four concurrent pushes; any failure blocks the release. Packing remains ordered
  within each precision because the managed packages consume the freshly packed platform/binding packages.
  `test-build-configuration` runs source/fixture checks independently of pack; `test-binding-packages` validates
  both precisions after pack, alongside consumers. Both gate deployment even on revision-only tags and belong
  in both reuse family lists. Binding checks download the matching fork SDK only, rather than all of godot-bin.
  Upload NuGet artifacts with compression disabled because their payloads are already compressed.
- Consumers that need both native outputs and NuGet packages use `actions/download-ci-inputs` to download them
  concurrently and move them into `godot/` and `packages/`, merging directories created by source checks.
  Require both artifacts before staging either.
  The MAUI job caches workload installer downloads by runner image, SDK and workload set; expose the cached
  nupkgs as a flat NuGet source alongside nuget.org, never `--from-cache`: the download command can omit manifests
  required by the pinned workload set. Still run installation to preserve Windows MSI registration.
  Changing its workload list requires a cache-key bump.
- iOS device/Simulator arm64 archives build in both precisions through `build-natives.yml`; release completeness
  and staging must require all four archives. Pack uploads scoped iOS inputs, excluding raw archives from godot-bin.
  The Windows MAUI job packs the precision-neutral iOS host and all MAUI TFMs. Its .NET SDK/workload pins must
  match the Xcode selected by `ios-smoke`. Three Simulator cells cover single Debug pixels, double Release showcase
  and single Debug MAUI; keep `iOS Smoke` in both reuse family lists and the deployment gate. Device signing
  stays with the app developer; CI publishes native packages and downloadable Simulator apps.
  Simulator first boot and app testing have separate time budgets; keep installation bounded and always
  shut down the created device, including after boot/test failure. Preserve both boot and app logs.
- `scripts/release.nu` is the normal local release path: bump `TwoDogRevision`, optionally `NativesRevision`
  with `--native`, sign the commit and atomically push main + tag. A main push with its matching version tag
  delegates testing/deployment to the tag run; keep the release-push gates on independent branch jobs.
  `release.yml` is the emergency/manual fallback with a `native` boolean. It tags `v<TwoDogVersion>` and
  dispatches `ci.yml` on the tag, because tags pushed with `GITHUB_TOKEN` start no workflows.
  When the tag's parent passed CI and the tag only bumps
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

- After changing `scripts/release.nu`, run `uv run python -m unittest discover -s tests/releasing -v`
  with Nushell installed. CI runs `release-push.test.sh` and `release-workflow.test.sh` with the other helpers.
- `test-with-watchdog.sh <Config> [idle-seconds] [budget-seconds]` runs `dotnet test` under a hang watchdog
  that dumps the test processes. It sets `TWODOG_GODOT_LOG_DIR` to `twodog.tests/TestResults/godot-<Config>`,
  so failed runs upload Godot's native logs with the test results. `TWODOG_TEST_FILTER` adds a `--filter`.
- After changing the web or Blazor smoke scripts, run `node --test .github/scripts/*.test.mjs`.
- Test helper variables: `TWODOG_CDP_DEBUG=1` logs CDP traffic in the browser smoke waiter;
  `TWODOG_TEST_VULKAN_LOADER` (set by `setup-windows-software-rendering.ps1`) copies a Vulkan loader beside
  the Windows NUnit rendering host; `TWODOG_TEST_RESTORE_PACKAGES` overrides the Android package tests'
  restore cache; `TWODOG_WEB_TARGETS` overrides the targets file the side-module export tests read.

## Checking runs

- A green run can hide skipped jobs: confirm that the test jobs ran, e.g. with `gh run view <run-id>`.
- Read every page of jobs when reusing a run; native build matrices can fill the first 100 results.
