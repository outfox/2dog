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

# Release from the clean branch of the Godot line (4.7, 4.8), including any local commits that have not been pushed yet.
def main [
    --native       # Also increment NativesRevision (native NuGet packages; cached engine binaries stay reusable).
    --revision: int # Set TwoDogRevision explicitly instead of continuing the sequence.
    --dry-run      # Check and preview without changing files, commits, tags or remote refs.
] {
    cd ($env.FILE_PWD | path dirname)
    let props = (open --raw Directory.Build.props)
    let version = (property $props GodotVersion)
    # Godot's own C# package versions. An unnumbered label (master's "beta" between betas) would sort above the
    # numbered prereleases after it, so only releases and numbered prereleases ship.
    if $version !~ '^\d+\.\d+\.\d+(-(beta|rc)\.\d+)?$' {
        error make {msg: $"GodotVersion ($version) must be a Godot release or numbered prerelease: 4.7.2, 4.8.0-beta.1, 4.8.0-rc.1."}
    }
    let branch = ($version | parse --regex '^(?<line>\d+\.\d+)' | get 0.line)
    if (git branch --show-current) != $branch {
        error make {msg: $"Release Godot ($version) from the ($branch) branch."}
    }
    if (git status --porcelain | is-not-empty) {
        error make {msg: 'Commit or stash pending changes, including submodule changes, before releasing.'}
    }

    let remote = (git ls-remote origin $"refs/heads/($branch)" 'refs/tags/v*' | lines | parse '{sha}	{ref}')
    let remote_branch = ($remote | where ref == $"refs/heads/($branch)")
    if ($remote_branch | length) != 1 {
        error make {msg: $"Could not find origin/($branch)."}
    }
    # One revision sequence across every Godot line: continue after the highest revision any release tag uses.
    let current = (property $props TwoDogRevision | into int)
    let tagged = ($remote | where ref =~ '^refs/tags/v.*\.\d+$' | get ref | each {|ref| $ref | split row '.' | last | into int })
    let highest = ($tagged | append $current | math max)
    let next = ($revision | default ($highest + 1))
    if $next <= $highest {
        error make {msg: $"Revision must be greater than ($highest), the highest revision released on any line."}
    }
    let natives = (property $props NativesRevision | into int)
    let tag = $"v($version).($next)"
    let next_natives = if $native { $natives + 1 } else { $natives }
    if ($remote | where ref == $"refs/tags/($tag)" | is-not-empty) or (git tag --list $tag | is-not-empty) {
        error make {msg: $"Tag ($tag) already exists."}
    }
    # Refuse a stale/diverged checkout; the atomic push also catches races after this check.
    git merge-base --is-ancestor $remote_branch.0.sha HEAD | ignore

    print $"Release ($tag): TwoDogRevision ($current) -> ($next), NativesRevision ($natives) -> ($next_natives)"
    let updated = ($props
        | str replace $'<TwoDogRevision>($current)</TwoDogRevision>' $'<TwoDogRevision>($next)</TwoDogRevision>'
        | str replace $'<NativesRevision>($natives)</NativesRevision>' $'<NativesRevision>($next_natives)</NativesRevision>')
    if $dry_run {
        print $"Would sign a version bump commit, tag ($tag), and atomically push ($branch) and the tag to origin."
        return
    }

    $updated | save --force Directory.Build.props
    git add -- Directory.Build.props | ignore
    git -c commit.gpgsign=true commit -m $"chore: release ($tag)" | print
    git tag $tag | ignore
    print $"Pushing ($branch) and ($tag); the tag run tests and publishes this release."
    let push = [--atomic origin $"HEAD:refs/heads/($branch)" $"refs/tags/($tag):refs/tags/($tag)"]
    try {
        git push ...$push | print
    } catch {|err|
        error make {msg: $"($err.msg)\nLocal commit and tag remain. After resolving the push failure, retry:\ngit push ($push | str join ' ')"}
    }
    print $"Follow CI: https://github.com/outfox/2dog/actions?query=branch%3A($tag)"
}
