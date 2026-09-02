#!/usr/bin/env bash
# Installs the freshly packed `2dog` tool from the CI artifacts, scaffolds a project with it in a directory whose
# name contains a space, checks it with `2dog doctor`, and builds and tests the result against the same artifacts.
# Expects: packages/ (nuget-packages artifact) and godot/ (godot-bin artifact) in the checkout, the .NET 10 SDK.
set -euo pipefail

# The assertions grep for plain prefixes; keep colour off whatever the runner exports (and log what it does).
echo "colour env: $(env | grep -iE '^(FORCE_COLOR|CLICOLOR|CLICOLOR_FORCE|NO_COLOR|TERM)=' | tr '\n' ' ' || true)"
unset FORCE_COLOR CLICOLOR_FORCE
export NO_COLOR=1

# Native path on Windows (Git Bash would hand NuGet /c/... which it reads as C:\c\...).
root="$(cd "$(dirname "$0")/../.." && (pwd -W 2>/dev/null || pwd))"
version="$(dotnet msbuild "$root/twodog/twodog.csproj" -getProperty:TwoDogVersion)"
work="${RUNNER_TEMP:-/tmp}/2dog-tool-smoke"
rm -rf "$work"
mkdir -p "$work/dir with space" "$work/toolbin"

# packageSourceMapping pins every 2dog.* package to the artifact feed, so an identically numbered package on
# nuget.org can never shadow the bits under test.
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$root/packages" />
    <add key="godot" value="$root/godot/bin/GodotSharp/Tools/nupkgs" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="2dog*" /></packageSource>
    <packageSource key="godot"><package pattern="*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF

echo "::group::install 2dog $version"
dotnet tool install 2dog --version "$version" --tool-path "$work/toolbin" --configfile "$work/nuget.config"
tool="$work/toolbin/2dog"
"$tool" version --plain
echo "::endgroup::"

cd "$work/dir with space"

echo "::group::scaffold"
"$tool" new "Smoke Game" --desktop --tests -y --no-restore 2> stderr.txt
cat stderr.txt
grep -q "note: project name adjusted" stderr.txt
test -f SmokeGame/SmokeGame.slnx
test -f SmokeGame/Directory.Build.props
test -f SmokeGame/SmokeGame.2dog/SmokeGame.2dog.csproj
test -f SmokeGame/SmokeGame.tests/SmokeGame.tests.csproj
"$tool" add SmokeGame --web --dry-run --json --no-restore | jq -e '.ok and (.actions | length) > 0 and .dryRun' > /dev/null
echo "::endgroup::"

echo "::group::usage errors and help"
set +e
"$tool" add SmokeGame --dekstop 2> usage.txt; status=$?
set -e
test "$status" -eq 1
grep -q "did you mean --desktop" usage.txt
"$tool" new --help | grep -q -- "--output"
"$tool" doctor --list-checks | grep -q "env.dotnet-sdk"
echo "::endgroup::"

echo "::group::build and test the scaffolded project"
cp "$work/nuget.config" SmokeGame/nuget.config
dotnet build SmokeGame/SmokeGame.slnx -c Debug
dotnet test SmokeGame/SmokeGame.tests -c Debug --no-build
echo "::endgroup::"

echo "::group::doctor"
"$tool" doctor SmokeGame --json --offline | tee doctor.json | jq -e '.doctor.summary.fail == 0' > /dev/null
rm SmokeGame/SmokeGame.tests/.gdignore
set +e
"$tool" doctor SmokeGame --offline; status=$?
set -e
test "$status" -eq 3
"$tool" doctor SmokeGame --offline --fix
"$tool" doctor SmokeGame --offline --build SmokeGame.2dog
echo "::endgroup::"

echo "tool smoke passed"
