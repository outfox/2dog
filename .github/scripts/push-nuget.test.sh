#!/usr/bin/env bash
set -euo pipefail
script=$(cd "$(dirname "$0")" && pwd)/push-nuget.sh
scratch=$(mktemp -d)
trap 'rm -f "$scratch/bin/dotnet" "$scratch/feed/"* "$scratch/calls/"*; rmdir "$scratch/bin" "$scratch/feed" "$scratch/calls" "$scratch"' EXIT
mkdir "$scratch/bin" "$scratch/feed" "$scratch/calls"
export PATH="$scratch/bin:$PATH" NUGET_API_KEY=test-key CALLS="$scratch/calls"
cat > "$scratch/bin/dotnet" <<'MOCK'
#!/usr/bin/env bash
[[ "$1" == nuget && "$2" == push ]] || exit 1
printf '%s\n' "$3" > "$CALLS/started.$BASHPID"
# A serial publisher cannot release the first four workers.
for attempt in $(seq 1 100); do
  [ "$(find "$CALLS" -type f | wc -l)" -ge 4 ] && break
  sleep 0.02
done
[ "$(find "$CALLS" -type f | wc -l)" -ge 4 ] || exit 1
[[ "$3" != *broken* ]]
MOCK
chmod +x "$scratch/bin/dotnet"
for name in one two three four 'with space' five; do touch "$scratch/feed/$name.nupkg"; done
touch "$scratch/feed/one.snupkg"
bash "$script" "$scratch/feed"
[ "$(find "$CALLS" -type f | wc -l)" -eq 6 ] || { echo 'Wrong packages pushed'; exit 1; }
touch "$scratch/feed/broken.nupkg"
if bash "$script" "$scratch/feed"; then echo 'Ignored a failed package push'; exit 1; fi
echo 'PASS: concurrent NuGet publishing preserves paths and propagates failures'
