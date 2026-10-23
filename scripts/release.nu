#!/usr/bin/env nu
# Commit pending release changes first, then run: nu scripts/release.nu --native

def --wrapped git [...args: string] {
    let result = (^git ...$args | complete)
    if $result.exit_code != 0 {
        error make {msg: $"git ($args | str join ' ') failed:\n($result.stderr)($result.stdout)"}
    }
    $result.stdout | str trim
}

def property [props: string, name: string] {
    let values = ($props | parse --regex ('<' + $name + '>(?<value>[^<]+)</' + $name + '>'))
    if ($values | length) != 1 {
        error make {msg: $"Expected one <($name)> in Directory.Build.props"}
    }
    $values.0.value
}

# Release from clean main, including any local commits that have not been pushed yet.
def main [
    --native       # Also increment NativesRevision (native NuGet packages; cached engine binaries stay reusable).
    --revision: int # Set TwoDogRevision explicitly instead of incrementing it.
    --dry-run      # Check and preview without changing files, commits, tags or remote refs.
] {
    cd ($env.FILE_PWD | path dirname)
    if (git branch --show-current) != 'main' {
        error make {msg: 'Release from main.'}
    }
    if (git status --porcelain | is-not-empty) {
        error make {msg: 'Commit or stash pending changes, including submodule changes, before releasing.'}
    }

    let props = (open --raw Directory.Build.props)
    let version = (property $props GodotVersion)
    if $version !~ '^\d+\.\d+\.\d+$' {
        error make {msg: 'GodotVersion must have three numeric components.'}
    }
    let current = (property $props TwoDogRevision | into int)
    let natives = (property $props NativesRevision | into int)
    let next = ($revision | default ($current + 1))
    if $next <= $current {
        error make {msg: $"Revision must be greater than ($current)."}
    }
    let tag = $"v($version).($next)"
    let next_natives = if $native { $natives + 1 } else { $natives }
    let remote = (git ls-remote origin refs/heads/main $"refs/tags/($tag)" | lines | parse '{sha}	{ref}')
    if ($remote | where ref == $"refs/tags/($tag)" | is-not-empty) or (git tag --list $tag | is-not-empty) {
        error make {msg: $"Tag ($tag) already exists."}
    }
    let remote_main = ($remote | where ref == refs/heads/main)
    if ($remote_main | length) != 1 {
        error make {msg: 'Could not find origin/main.'}
    }
    # Refuse a stale/diverged checkout; the atomic push also catches races after this check.
    git merge-base --is-ancestor $remote_main.0.sha HEAD | ignore

    print $"Release ($tag): TwoDogRevision ($current) -> ($next), NativesRevision ($natives) -> ($next_natives)"
    let updated = ($props
        | str replace $'<TwoDogRevision>($current)</TwoDogRevision>' $'<TwoDogRevision>($next)</TwoDogRevision>'
        | str replace $'<NativesRevision>($natives)</NativesRevision>' $'<NativesRevision>($next_natives)</NativesRevision>')
    if $dry_run {
        print $"Would sign a version bump commit, tag ($tag), and atomically push main and the tag to origin."
        return
    }

    $updated | save --force Directory.Build.props
    git add -- Directory.Build.props | ignore
    git -c commit.gpgsign=true commit -m $"chore: release ($tag)" | print
    git tag $tag | ignore
    print $"Pushing main and ($tag); the tag run tests and publishes this release."
    try {
        git push --atomic origin HEAD:refs/heads/main $"refs/tags/($tag):refs/tags/($tag)" | print
    } catch {|err|
        error make {msg: $"($err.msg)\nLocal commit and tag remain. After resolving the push failure, retry:\ngit push --atomic origin HEAD:refs/heads/main refs/tags/($tag):refs/tags/($tag)"}
    }
    print $"Follow CI: https://github.com/outfox/2dog/actions?query=branch%3A($tag)"
}
