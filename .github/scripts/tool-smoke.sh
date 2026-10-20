#!/usr/bin/env bash
# Installs the freshly packed `2dog` tool and `2dog.templates` from the CI artifacts, scaffolds projects in a directory whose
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
cache="$(cd "$work" && (pwd -W 2>/dev/null || pwd))/nuget-packages"

# packageSourceMapping pins every 2dog.* package to the artifact feed, so an identically numbered package on
# nuget.org can never shadow the bits under test. Keep the cache under $work but outside the scaffolded game:
# package sources such as 2dog.xunit's compile-in helpers must never enter the game's default compile glob.
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$cache" />
  </config>
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

echo "::group::template package"
tool_package="$root/packages/2dog.$version.nupkg"
template_package="$root/packages/2dog.templates.$version.nupkg"
unzip -p "$tool_package" '*.nuspec' > "$work/tool.nuspec"
unzip -p "$template_package" '*.nuspec' > "$work/template.nuspec"
grep -q 'packageType name="DotnetTool"' "$work/tool.nuspec"
grep -q 'packageType name="Template"' "$work/template.nuspec"
if grep -q 'packageType name="Template"' "$work/tool.nuspec" \
  || grep -q 'packageType name="DotnetTool"' "$work/template.nuspec" \
  || unzip -Z1 "$tool_package" | grep '^content/' \
  || unzip -Z1 "$template_package" | grep -E '^(lib|tools|build|buildTransitive)/'; then
  echo "::error::The tool and template package contents are not separated."
  exit 1
fi
# A private hive keeps the smoke independent of templates installed on the runner.
hive="$work/template-hive"
dotnet new install "$template_package" --debug:custom-hive "$hive"
dotnet new 2dog -n TemplateGame --web false --xunit true --nunit true --skipRestore true --debug:custom-hive "$hive"
test -f TemplateGame/TemplateGame.2dog/.gdignore
test -f TemplateGame/.vscode/launch.json
test -f TemplateGame/.editorconfig
grep -q "$version" TemplateGame/Directory.Build.props
dotnet build TemplateGame/TemplateGame.slnx -c Debug
dotnet new uninstall 2dog.templates --debug:custom-hive "$hive"
echo "::endgroup::"

echo "::group::scaffold"
"$tool" new "Smoke Game" --generic --tests --nunit -y --no-restore 2> stderr.txt
cat stderr.txt
grep -q "note: project name adjusted" stderr.txt
test -f SmokeGame/SmokeGame.slnx
test -f SmokeGame/Directory.Build.props
test -f SmokeGame/SmokeGame.2dog/SmokeGame.2dog.csproj
test -f SmokeGame/SmokeGame.xunit/SmokeGame.xunit.csproj
test -f SmokeGame/SmokeGame.nunit/SmokeGame.nunit.csproj
test -f SmokeGame/SmokeGame.nunit/.gdignore
"$tool" add SmokeGame --web --dry-run --json --no-restore | jq -e '.ok and (.actions | length) > 0 and .dryRun' > /dev/null
echo "::endgroup::"

echo "::group::usage errors and help"
set +e
"$tool" add SmokeGame --genric 2> usage.txt; status=$?
set -e
test "$status" -eq 1
grep -q "did you mean --generic" usage.txt
"$tool" new --help | grep -q -- "--output"
"$tool" doctor --list-checks | grep -q "env.dotnet-sdk"
echo "::endgroup::"

echo "::group::build and test the scaffolded project"
cp "$work/nuget.config" SmokeGame/nuget.config
# Runs from the project root, as the scaffold's next steps do: dotnet test takes its runner (Microsoft.Testing.Platform)
# from the global.json above the working directory, not above the project.
test_hosts() {
  (cd SmokeGame && dotnet test SmokeGame.xunit "$@" --no-build && dotnet test SmokeGame.nunit "$@" --no-build)
}
for config in Debug Release Editor; do
  dotnet build SmokeGame/SmokeGame.slnx -c "$config"
  test_hosts -c "$config"
done
echo "::endgroup::"

echo "::group::desktop and tests with unused optional hosts"
"$tool" add SmokeGame --web --android -y --no-restore
# Disabling SDK workload resolution proves this graph does not evaluate either
# optional host, even on runners where a workload happens to be installed.
dotnet build SmokeGame/SmokeGame.slnx -c Debug -p:MSBuildEnableWorkloadResolver=false
test_hosts -c Debug
echo "::endgroup::"

echo "::group::doctor"
"$tool" doctor SmokeGame --json --offline | tee doctor.json | jq -e '.doctor.summary.fail == 0' > /dev/null
rm SmokeGame/SmokeGame.xunit/.gdignore
set +e
"$tool" doctor SmokeGame --offline; status=$?
set -e
test "$status" -eq 3
"$tool" doctor SmokeGame --offline --fix
"$tool" doctor SmokeGame --offline --build SmokeGame.2dog
echo "::endgroup::"

echo "tool smoke passed"
