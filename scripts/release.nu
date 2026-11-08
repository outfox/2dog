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

# Files whose changes alter the native packages (platform packages, their staging and metadata).
const NATIVE_INPUTS = [godot platforms nuget build scripts/stage_natives_release.py]

# Release the current branch: `main` and stable lines (4.7) publish stable versions, a labeled line (4.8.0-beta)
# publishes prereleases (4.8.0.153-beta). Every release takes the next number after all release tags on origin.
def main [
    --dry-run  # Check and preview without changing files, commits, tags or remote refs.
    --no-sign  # Do not force a signed commit (the CI fallback has no signing key).
] {
    cd ($env.FILE_PWD | path dirname)
    let branch = (git branch --show-current)
    let line = ($branch | parse --regex '^(?<numbers>\d+\.\d+(?:\.\d+)?)(?:-(?<label>[A-Za-z]+))?$')
    if $branch != 'main' and ($line | is-empty) {
        error make {msg: $"Release from main or a release branch such as 4.7 or 4.8.0-beta \(on '($branch)')."}
    }
    if (git status --porcelain | is-not-empty) {
        error make {msg: 'Commit or stash pending changes, including submodule changes, before releasing.'}
    }

    let props = (open --raw Directory.Build.props)
    let version = (property $props GodotVersion)
    if $version !~ '^\d+\.\d+\.\d+$' {
        error make {msg: 'GodotVersion must have three numeric components.'}
    }
    let label = if ($line | is-empty) { '' } else { $line.0.label | default '' }
    if ($line | is-not-empty) and not ($"($version)." | str starts-with $"($line.0.numbers).") {
        error make {msg: $"Branch ($branch) releases Godot ($line.0.numbers), but GodotVersion is ($version)."}
    }
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

    print $"Release ($tag): TwoDogRevision ($current) -> ($next), NativesRevision ($natives) -> ($next_natives)"
    let updated = ($props
        | set-property TwoDogRevision $"($next)"
        | set-property NativesRevision $"($next_natives)"
        | set-property TwoDogPrerelease $label)
    if $dry_run {
        print $"Would commit the version bump, tag ($tag), and atomically push ($branch) and the tag to origin."
        return
    }

    $updated | save --force Directory.Build.props
    git add -- Directory.Build.props | ignore
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
    print $"Promote to nuget.org afterwards: gh workflow run publish.yml --ref ($tag) -f feed=nuget"
}
