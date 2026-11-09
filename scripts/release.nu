#!/usr/bin/env nu
# Commit pending release changes first, then run: nu scripts/release.nu

def --wrapped git [...args: string] {
    let result = (^git ...$args | complete)
    if $result.exit_code != 0 {
        error make {msg: $"git ($args | str join ' ') failed:\n($result.stderr)($result.stdout)"}
    }
    $result.stdout | str trim
}

def property [props: string, name: string] {
    let values = ($props | parse --regex ('<' + $name + '>(?<value>[^<]*)</' + $name + '>'))
    if ($values | length) != 1 {
        error make {msg: $"Expected one <($name)> in Directory.Build.props"}
    }
    $values.0.value
}

def set-property [name: string, value: string]: string -> string {
    $in | str replace --regex ('<' + $name + '>[^<]*</' + $name + '>') $'<($name)>($value)</($name)>'
}

# godot/version.py at the pinned commit: the checkout (a clean tree has it at that commit) or the fork on GitHub.
def godot-version-py [pinned: string] {
    if ('godot/version.py' | path exists) { return (open --raw godot/version.py) }
    let fork = (git config --file .gitmodules submodule.godot.url | parse --regex 'github\.com[:/](?<repo>.+?)(?:\.git)?$')
    if ($fork | is-empty) { error make {msg: 'Check out godot: its fork is not on GitHub.'} }
    http get --raw $"https://raw.githubusercontent.com/($fork.0.repo)/($pinned)/version.py"
}

# Godot's release numbers, its status ("stable", "beta2") and the C# package version Godot's build_assemblies.py
# derives from them ("beta2" -> 4.8.0-beta.2).
def godot-version [py: string] {
    let field = {|name| $py | parse --regex ('(?m)^' + $name + ' = "?(?<value>[^"\r\n]*)"?') | get 0.value }
    let release = $"(do $field major).(do $field minor).(do $field patch)"
    let status = (do $field status)
    let numbered = ($status | parse --regex '^(?<word>.*?)(?<n>\d+)$')
    let csharp = if $status == 'stable' { $release } else if ($numbered | is-empty) { $"($release)-($status)" } else {
        $"($release)-($numbered.0.word).($numbered.0.n)"
    }
    # Scaffolded games pin Godot.NET.Sdk to it; nuget.org only has Godot's numbered prereleases.
    if $status != 'stable' and $status !~ '^(dev|beta|rc)\d+$' {
        error make {msg: $"godot/version.py has status \"($status)\", which builds Godot.NET.Sdk ($csharp): nuget.org has only numbered Godot prereleases \(status = \"beta2\")."}
    }
    {release: $release, status: $status, csharp: $csharp}
}

# GodotVersion and the committed Godot.NET.Sdk pins follow the fork (an Sdk attribute cannot use a property).
def repin [csharp: string]: string -> string {
    $in | str replace --all --regex '(Godot\.NET\.Sdk(?:/|" Version="))[0-9][^"]*' ('${1}' + $csharp)
}

# The csproj files whose Godot.NET.Sdk pin differs from csharp.
def repinned-files [csharp: string] {
    (^git grep -l -E 'Godot\.NET\.Sdk(/|" Version=")[0-9]' -- '*.csproj' | complete | get stdout | lines
        | where {|file| $file != '' and (open --raw $file) != (open --raw $file | repin $csharp) })
}

# Files whose changes alter the native packages (platform packages, their staging and metadata).
const NATIVE_INPUTS = [godot platforms nuget build scripts/stage_natives_release.py]

# Release the current branch at the Godot version its fork builds: a stable Godot publishes stable versions (or a
# 2dog prerelease labeled by the branch, 4.7-rc), a Godot prerelease publishes under Godot's status (4.8.0.155-beta2).
# Every release takes the next number after all release tags on origin.
def main [
    --dry-run  # Check and preview without changing files, commits, tags or remote refs.
    --no-sign  # Do not force a signed commit (the CI fallback has no signing key).
    --sync     # Only align GodotVersion, TwoDogPrerelease and the Godot.NET.Sdk pins with godot/version.py (after a fork bump).
] {
    cd ($env.FILE_PWD | path dirname)
    let branch = (git branch --show-current)
    # The 2dog tool treats exactly these labels as official prereleases.
    let line = ($branch | parse --regex '^(?<numbers>\d+\.\d+(?:\.\d+)?)(?:-(?<label>dev|beta|rc))?$')
    if $sync {
        # The checked-out fork (so an uncommitted bump syncs), else version.py at the pinned commit (CI).
        let godot = (godot-version (godot-version-py (git rev-parse HEAD:godot)))
        let props = (open --raw Directory.Build.props)
        # Off a release branch (CI's detached checkouts), a stable Godot keeps the committed branch label.
        let label = if $godot.status != 'stable' { $godot.status } else if ($line | is-empty) { property $props TwoDogPrerelease } else { $line.0.label | default '' }
        let repinned = (repinned-files $godot.csharp)
        let props = ($props | set-property GodotVersion $godot.csharp | set-property TwoDogPrerelease $label)
        let summary = $"GodotVersion ($godot.csharp), TwoDogPrerelease '($label)', Godot.NET.Sdk pins in ($repinned | length) files."
        if $dry_run { print $"Would set ($summary)"; return }
        $props | save --force --raw Directory.Build.props
        for file in $repinned { open --raw $file | repin $godot.csharp | save --force --raw $file }
        print $"Set ($summary)"
        return
    }
    if $branch != 'main' and ($line | is-empty) {
        error make {msg: $"Release from main or a release branch such as 4.7, 4.8 or 4.7-rc \(on '($branch)')."}
    }
    if (git status --porcelain | is-not-empty) {
        error make {msg: 'Commit or stash pending changes, including submodule changes, before releasing.'}
    }

    let props = (open --raw Directory.Build.props)
    let pinned = (git rev-parse HEAD:godot)
    let godot = (godot-version (godot-version-py $pinned))
    let version = $godot.release
    if ($line | is-not-empty) and not ($"($version)." | str starts-with $"($line.0.numbers).") {
        error make {msg: $"Branch ($branch) releases Godot ($line.0.numbers), but the godot submodule is Godot ($godot.csharp)."}
    }
    let label = if $godot.status != 'stable' { $godot.status } else if ($line | is-empty) { '' } else { $line.0.label | default '' }
    let current = (property $props TwoDogRevision | into int)
    let natives = (property $props NativesRevision | into int)

    let remote = (git ls-remote origin $"refs/heads/($branch)" 'refs/tags/v*' | lines | parse '{sha}	{ref}')
    let releases = ($remote
        | where ref !~ '\^\{\}$'
        | get ref
        | parse --regex '^refs/tags/v(?<godot>\d+\.\d+\.\d+)\.(?<n>\d+)(?:-[0-9A-Za-z.-]+)?$')
    let next = ([$current ...($releases | get n | into int)] | math max) + 1
    let suffix = if $label == '' { '' } else { $"-($label)" }
    let tag = $"v($version).($next)($suffix)"
    if (git tag --list $tag | is-not-empty) {
        error make {msg: $"Tag ($tag) already exists."}
    }
    let remote_branch = ($remote | where ref == $"refs/heads/($branch)")
    if ($remote_branch | length) != 1 {
        error make {msg: $"Could not find origin/($branch)."}
    }
    # Refuse a stale/diverged checkout; the atomic push also catches races after this check.
    git merge-base --is-ancestor $remote_branch.0.sha HEAD | ignore

    # NativesRevision names the release that built the natives; reuse them while the inputs match that release.
    let built_ref = ('^refs/tags/v' + ($"($version).($natives)" | str replace --all '.' '\.') + '(-[0-9A-Za-z.-]+)?(\^\{\})?$')
    let built = ($remote | where ref =~ $built_ref)
    let reuse = if ($built | is-empty) { false } else {
        # An annotated tag's peeled commit (^{}) sorts after the tag object.
        let sha = ($built | sort-by ref | last | get sha)
        if (^git cat-file -e $"($sha)^{commit}" | complete).exit_code != 0 {
            git fetch --quiet --no-tags origin $sha | ignore
        }
        (^git diff --quiet $sha HEAD -- ...$NATIVE_INPUTS | complete).exit_code == 0
    }
    let next_natives = if $reuse { $natives } else { $next }

    let repinned = (repinned-files $godot.csharp)
    let was = (property $props GodotVersion)
    print ($"Release ($tag): TwoDogRevision ($current) -> ($next), NativesRevision ($natives) -> ($next_natives)"
        + (if $was == $godot.csharp { '' } else { $", GodotVersion ($was) -> ($godot.csharp)" })
        + (if ($repinned | is-empty) { '' } else { $", Godot.NET.Sdk ($godot.csharp) in ($repinned | str join ', ')" }))
    let updated = ($props
        | set-property GodotVersion $godot.csharp
        | set-property TwoDogRevision $"($next)"
        | set-property NativesRevision $"($next_natives)"
        | set-property TwoDogPrerelease $label)
    # Old releases must keep resolving their godot commit after the fork's line branch (2dog-4.7) moves on.
    let fork_tag = [-C godot push (git config --file .gitmodules submodule.godot.url) $"($pinned):refs/tags/2dog-($tag)"]
    if $dry_run {
        print $"Would tag the fork's godot ($pinned) as 2dog-($tag), commit the version bump, tag ($tag), and atomically push ($branch) and the tag to origin."
        return
    }

    if ('godot/.git' | path exists) {
        git ...$fork_tag | ignore
    } else {
        print --stderr $"warning: godot is not checked out; protect the pinned commit by hand: git ($fork_tag | str join ' ')"
    }
    $updated | save --force Directory.Build.props
    for file in $repinned { open --raw $file | repin $godot.csharp | save --force --raw $file }
    git add -- Directory.Build.props ...$repinned | ignore
    let sign = if $no_sign { [] } else { [-c commit.gpgsign=true] }
    git ...$sign commit -m $"chore: release ($tag)" | print
    git tag $tag | ignore
    print $"Pushing ($branch) and ($tag); the tag run tests the release and stages it on int.nugettest.org."
    let push = [push --atomic origin $"HEAD:refs/heads/($branch)" $"refs/tags/($tag):refs/tags/($tag)"]
    try {
        git ...$push | print
    } catch {|err|
        error make {msg: $"($err.msg)\nLocal commit and tag remain. After resolving the push failure, retry:\ngit ($push | str join ' ')"}
    }
    print $"Follow CI: https://github.com/outfox/2dog/actions?query=branch%3A($tag)"
    print $"Promote to nuget.org afterwards: gh workflow run publish.yml -f tag=($tag) -f feed=nuget"
}
