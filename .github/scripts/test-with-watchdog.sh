#!/usr/bin/env bash
# Runs `dotnet test` for one configuration under a hang watchdog that dumps the processes that
# actually run tests. vstest's --blame-hang only dumps its own testhost, but xunit v3 executes the
# assembly in a child process and HelperToolTestBed spawns 2dog.import grandchildren; on a hang
# those are the stacks that matter, so we createdump each of them before killing the tree.
# Usage: test-with-watchdog.sh <Configuration> [timeout-seconds]
set -euo pipefail

config="$1"
limit="${2:-120}"
pattern='twodog\.tests\.dll|2dog\.import\.dll|testhost'

if [[ "${RUNNER_OS:-}" == "Windows" ]]; then
  exec dotnet test 2dog.tests.slnf -c "$config" --no-restore \
    --blame-crash --blame-crash-dump-type full \
    --blame-hang-timeout "${limit}s" --blame-hang-dump-type full
fi

# `Microsoft.NETCore.App 10.0.x [/usr/share/dotnet/shared/Microsoft.NETCore.App]` -> dir/version
runtime_line="$(dotnet --list-runtimes | grep '^Microsoft.NETCore.App 10\.' | tail -1)"
runtime_ver="${runtime_line#Microsoft.NETCore.App }"; runtime_ver="${runtime_ver%% *}"
runtime_base="${runtime_line#*[}"; runtime_base="${runtime_base%]}"
createdump="$runtime_base/$runtime_ver/createdump"
dumps="twodog.tests/TestResults/watchdog-$config"

# Ubuntu's Yama scope only lets ancestors ptrace; createdump runs as a sibling.
if [[ "${RUNNER_OS:-}" == "Linux" ]]; then
  sudo sysctl -q -w kernel.yama.ptrace_scope=0 || true
fi

dotnet test 2dog.tests.slnf -c "$config" --no-restore \
  --blame-crash --blame-crash-dump-type full &
test_pid=$!

elapsed=0
while kill -0 "$test_pid" 2>/dev/null; do
  if (( elapsed >= limit )); then
    echo "::error::Test run ($config) exceeded ${limit}s; dumping test processes to $dumps"
    mkdir -p "$dumps"
    for pid in $(pgrep -f "$pattern" || true); do
      echo "--- pid $pid: $(tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null || ps -o command= -p "$pid")"
      "$createdump" --full -f "$dumps/hang_${pid}.dmp" "$pid" || echo "createdump failed for $pid"
    done
    pkill -TERM -P "$test_pid" || true
    kill -TERM "$test_pid" || true
    sleep 5
    pkill -KILL -f "$pattern" || true
    exit 1
  fi
  sleep 2
  elapsed=$((elapsed + 2))
done

wait "$test_pid"
