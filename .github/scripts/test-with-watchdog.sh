#!/usr/bin/env bash
# Runs `dotnet test` for one configuration under a watchdog that dumps the processes that actually
# run tests. vstest's --blame-hang only dumps its own testhost, but xunit v3 executes the assembly in
# a child process and HelperToolTestBed spawns 2dog.import grandchildren; on a hang those are the
# stacks that matter, so we createdump each of them before killing the tree.
# On macOS, createdump can itself hang; retain test output and a process snapshot instead.
# A hang is a run that prints nothing for the idle limit (per-test console lines are the progress
# signal; Windows uses vstest's per-test hang timeout); every run must also finish within the budget.
# Usage: test-with-watchdog.sh <Configuration> [idle-seconds] [budget-seconds]
set -euo pipefail

config="$1"
limit="${2:-120}"
budget="${3:-300}"
pattern='twodog\.tests\.dll|2dog\.import\.dll|testhost'

# Engine fixtures write Godot's verbose log here (native output never reaches the vstest log);
# the workflow uploads TestResults on failure. Godot on Windows needs a native path.
log_dir="$PWD/twodog.tests/TestResults/godot-$config"
if command -v cygpath >/dev/null 2>&1; then log_dir="$(cygpath -w "$log_dir")"; fi
export TWODOG_GODOT_LOG_DIR="$log_dir"

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

output="twodog.tests/TestResults/test-$config.log"
mkdir -p "$(dirname "$output")"
: > "$output"
case "${RUNNER_OS:-}" in
  Windows)
    dotnet test 2dog.tests.slnf -c "$config" --no-restore --logger 'console;verbosity=normal' \
      --blame-crash --blame-crash-dump-type full \
      --blame-hang-timeout "${limit}s" --blame-hang-dump-type full > >(tee "$output") 2>&1 & ;;
  macOS)
    dotnet test 2dog.tests.slnf -c "$config" --no-restore --logger 'console;verbosity=normal' \
      --blame > >(tee "$output") 2>&1 & ;;
  *)
    dotnet test 2dog.tests.slnf -c "$config" --no-restore --logger 'console;verbosity=normal' \
      --blame-crash --blame-crash-dump-type full > >(tee "$output") 2>&1 & ;;
esac
test_pid=$!

started=$SECONDS
progressed=$SECONDS
size=0
while kill -0 "$test_pid" 2>/dev/null; do
  current=$(wc -c < "$output")
  if (( current != size )); then
    size=$current
    progressed=$SECONDS
  fi
  reason=""
  if (( SECONDS - started >= budget )); then
    reason="took longer than ${budget}s"
  elif [[ "${RUNNER_OS:-}" != "Windows" ]] && (( SECONDS - progressed >= limit )); then
    reason="printed nothing for ${limit}s"
  fi
  if [[ -n "$reason" ]]; then
    echo "::error::Test run ($config) $reason; collecting diagnostics in $dumps"
    mkdir -p "$dumps"
    case "${RUNNER_OS:-}" in
      Windows)
        # MSYS signals reach only dotnet itself; end the whole tree by its Windows pid.
        taskkill //F //T //PID "$(cat "/proc/$test_pid/winpid")" || true
        exit 1 ;;
      macOS)
        ps -axo pid,ppid,state,etime,command > "$dumps/processes.txt" || true ;;
      *)
        for pid in $(pgrep -f "$pattern" || true); do
          echo "--- pid $pid: $(tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null || ps -o command= -p "$pid")"
          "$createdump" --full -f "$dumps/hang_${pid}.dmp" "$pid" || echo "createdump failed for $pid"
        done ;;
    esac
    pkill -TERM -P "$test_pid" || true
    kill -TERM "$test_pid" || true
    sleep 5
    pkill -KILL -f "$pattern" || true
    exit 1
  fi
  sleep 2
done

wait "$test_pid"
