#!/usr/bin/env bash
set -euo pipefail
script="$(cd "$(dirname "$0")" && pwd)/test-with-watchdog.sh"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
mkdir -p "$scratch/bin"
cat > "$scratch/bin/dotnet" <<'EOF'
#!/usr/bin/env bash
if [[ "$1" == --list-runtimes ]]; then
  echo 'Microsoft.NETCore.App 10.0.0 [/unused]'
else
  printf '<%s>' "$@" >> "$PROBE_LOG"
  printf '\n' >> "$PROBE_LOG"
fi
EOF
chmod +x "$scratch/bin/dotnet"
export PATH="$scratch/bin:$PATH"
export PROBE_LOG="$scratch/commands.log"
export RUNNER_OS=macOS
export TWODOG_TEST_FILTER='FullyQualifiedName~AvaloniaTests.&Precision!=Independent'
unset TWODOG_TEST_PROJECT
cd "$scratch"
bash "$script" Editor
grep -Fx '<build><2dog.tests.slnf><-c><Editor><--no-restore>' "$PROBE_LOG"
grep -Fx '<test><2dog.tests.slnf><-c><Editor><--no-build><--no-restore><--logger><console;verbosity=normal><--filter><FullyQualifiedName~AvaloniaTests.&Precision!=Independent><--blame>' "$PROBE_LOG"
export TWODOG_TEST_PROJECT=twodog.tests/twodog.tests.csproj
: > "$PROBE_LOG"
bash "$script" Release 60
grep -Fx '<build><twodog.tests/twodog.tests.csproj><-c><Release><--no-restore>' "$PROBE_LOG"
grep -Fx '<test><twodog.tests/twodog.tests.csproj><-c><Release><--no-build><--no-restore><--logger><console;verbosity=normal><--filter><FullyQualifiedName~AvaloniaTests.&Precision!=Independent><--blame>' "$PROBE_LOG"
echo 'PASS: watchdog builds and tests the selected project and preserves the complete filter'
