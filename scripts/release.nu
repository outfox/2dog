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
# Another line's commit is not what the checkout holds, so it is read from the fork's objects or GitHub.
def godot-version-py [pinned: string, --other-line] {
    if not $other_line and ('godot/version.py' | path exists) { return (open --raw godot/version.py) }
    if $other_line and ('godot/.git' | path exists) {
        let shown = (^git -C godot show $"($pinned):version.py" | complete)
        if $shown.exit_code == 0 { return $shown.stdout }
    }
    let fork = (git config --file .gitmodules submodule.godot.url | parse --regex 'github\.com[:/](?<repo>.+?)(?:\.git)?$')
    if ($fork | is-empty) { error make {msg: 'Check out godot: its fork is not on GitHub.'} }
    http get --raw $"https://raw.githubusercontent.com/($fork.0.repo)/($pinned)/version.py"
}

# Godot's release numbers, its status ("stable", "beta2"), the C# package version Godot's build_assemblies.py
# derives from them ("beta2" -> 4.8.0-beta.2) and the feature tag its editor writes into project.godot ("4.8").
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
    {release: $release, status: $status, csharp: $csharp, feature: $"(do $field major).(do $field minor)"}
}

# The committed text that follows the fork's Godot version: Godot.NET.Sdk pins (an Sdk attribute cannot use a
# property) and project.godot feature tags.
def retarget [godot: record]: string -> string {
    ($in
        | str replace --all --regex '(Godot\.NET\.Sdk(?:/|" Version="))[0-9][^"]*' ('${1}' + $godot.csharp)
        | str replace --all --regex '(config/features=PackedStringArray\(")[0-9]+\.[0-9]+' ('${1}' + $godot.feature))
}

# The files retarget would change.
def retargeted-files [godot: record] {
    (^git grep -l -E 'Godot\.NET\.Sdk(/|" Version=")[0-9]|config/features=PackedStringArray\("[0-9]' -- '*.csproj' '*project.godot'
        | complete | get stdout | lines
        | where {|file| $file != '' and (open --raw $file) != (open --raw $file | retarget $godot) })
}

def remote-refs [branch: string] {
    git ls-remote origin $"refs/heads/($branch)" 'refs/tags/v*' | lines | parse '{sha}	{ref}'
}

# Another Godot line built from this tree (godot-lines.json): its fork branch and commit, and the NativesRevision of
# its latest release. A line's releases are tagged off the branch, so that tag is where its natives state lives.
def line-target [name: string, remote: list] {
    let lines = (open godot-lines.json)
    if $name not-in ($lines | columns) {
        error make {msg: $"godot-lines.json has no line '($name)' \(it lists: ($lines | columns | str join ', '))."}
    }
    let tags = ($remote | where ref =~ ('^refs/tags/v' + ($name | str replace --all '.' '\.') + '\.\d+\.\d+(-[0-9A-Za-z.-]+)?$'))
    let natives = if ($tags | is-empty) { '0' } else {
        let latest = ($tags | sort-by {|tag| $tag.ref | parse --regex '\.(?<n>\d+)(?:-[0-9A-Za-z.-]+)?$' | get 0.n | into int } | last)
        if (^git cat-file -e $"($latest.sha)^{commit}" | complete).exit_code != 0 {
            git fetch --quiet --no-tags origin $latest.sha | ignore
        }
        property (git show $"($latest.sha):Directory.Build.props") NativesRevision
    }
    $lines | get $name | insert natives $natives
}

# Point the tree at a line's fork commit: the gitlink, the submodule checkout when there is one, and the fork branch
# CI checks the pin against.
def checkout-line [target: record] {
    git update-index --cacheinfo $"160000,($target.commit),godot" | ignore
    if ('godot/.git' | path exists) {
        if (^git -C godot cat-file -e $"($target.commit)^{commit}" | complete).exit_code != 0 {
            git -C godot fetch --quiet origin $target.branch | ignore
        }
        git -C godot checkout --quiet --detach $target.commit | ignore
    }
    open --raw .gitmodules | str replace --regex '(branch = )[^\r\n]*' ('${1}' + $target.branch) | save --force --raw .gitmodules
}

# Run work on a detached HEAD and return to start whatever happens: a line's commits must not land on the branch.
# The tree was clean, so discarding a half-applied line loses nothing.
def detached [start: string, work: closure] {
    git checkout --quiet --detach | ignore
    let failure = (try { do $work; null } catch {|err| $err.msg })
    git checkout --quiet --force $start | ignore
    if ('godot/.git' | path exists) { git submodule update --quiet godot | ignore }
    if $failure != null { error make {msg: $failure} }
}

# Release the current branch at the Godot version its fork builds: a stable Godot publishes stable versions (or a
# 2dog prerelease labeled by the branch, 4.7-rc), a Godot prerelease publishes under Godot's status (4.8.0.155-beta2).
# Every release takes the next number after all release tags on origin.
# main carries one Godot line in its tree; --line builds another one from godot-lines.json out of the same commit.
def main [
    --dry-run       # Check and preview without changing files, commits, tags or remote refs.
    --no-sign       # Do not force a signed commit (the CI fallback has no signing key).
    --sync          # Only align GodotVersion, TwoDogPrerelease, the Godot.NET.Sdk pins and feature tags with godot/version.py (after a fork bump). With --line: switch the working tree to that line.
    --ci            # With --line: push that line's tree to the branch ci/<line>/<commit>, which runs CI on it.
    --line: string  # Another Godot line from godot-lines.json (4.7) instead of the one in the tree.
] {
    cd ($env.FILE_PWD | path dirname)
    let branch = (git branch --show-current)
    # The 2dog tool treats exactly these labels as official prereleases.
    let named = ($branch | parse --regex '^(?<numbers>\d+\.\d+(?:\.\d+)?)(?:-(?<label>dev|beta|rc))?$')
    if $ci and $line == null {
        error make {msg: '--ci needs --line: CI for the line in the tree runs on the branch itself.'}
    }
    if $sync or $ci {
        if $line != null and (git status --porcelain | is-not-empty) {
            error make {msg: 'Commit or stash pending changes, including submodule changes, before switching lines.'}
        }
        let target = if $line == null { null } else { line-target $line (remote-refs $branch) }
        # The checked-out fork (so an uncommitted bump syncs), else version.py at the pinned commit (CI).
        let godot = if $target == null { godot-version (godot-version-py (git rev-parse HEAD:godot)) } else {
            godot-version (godot-version-py $target.commit --other-line)
        }
        if $target != null and not ($"($godot.release)." | str starts-with $"($line).") {
            error make {msg: $"godot-lines.json pins line ($line) to a commit of Godot ($godot.csharp)."}
        }
        let props = (open --raw Directory.Build.props)
        # Off a release branch (CI's detached checkouts), a stable Godot keeps the committed branch label; another
        # line never inherits the tree's label.
        let label = if $godot.status != 'stable' { $godot.status } else if $target != null { '' } else if ($named | is-empty) { property $props TwoDogPrerelease } else { $named.0.label | default '' }
        let retargeted = (retargeted-files $godot)
        let props = ($props | set-property GodotVersion $godot.csharp | set-property TwoDogPrerelease $label)
        let props = if $target == null { $props } else { $props | set-property NativesRevision $target.natives }
        let summary = ($"GodotVersion ($godot.csharp), TwoDogPrerelease '($label)', Godot.NET.Sdk pins and feature tags in ($retargeted | length) files"
            + (if $target == null { '.' } else { $", godot ($target.commit | str substring 0..<7) on ($target.branch), NativesRevision ($target.natives)." }))
        if $dry_run { print $"Would set ($summary)"; return }
        let apply = {||
            if $target != null { checkout-line $target }
            $props | save --force --raw Directory.Build.props
            for file in $retargeted { open --raw $file | retarget $godot | save --force --raw $file }
        }
        if not $ci {
            do $apply
            print $"Set ($summary)"
            if $target != null { print 'Back to the line in the tree: git reset --hard; git submodule update' }
            return
        }
        let source = (git rev-parse --short HEAD)
        let ref = $"ci/($line)/($source)"
        detached (if $branch == '' { git rev-parse HEAD } else { $branch }) {||
            do $apply
            git add -- Directory.Build.props .gitmodules ...$retargeted | ignore
            git commit --quiet -m $"ci: Godot ($line) line of ($source)" | ignore
            git push origin $"HEAD:refs/heads/($ref)" | ignore
        }
        print $"Pushed ($ref) with ($summary)"
        print $"Follow CI: https://github.com/outfox/2dog/actions?query=branch%3A($ref)"
        print $"Delete the branch afterwards: git push origin --delete ($ref)"
        return
    }
    if $line != null and $branch != 'main' {
        error make {msg: $"Another Godot line releases from main \(on '($branch)')."}
    }
    if $branch != 'main' and ($named | is-empty) {
        error make {msg: $"Release from main or a release branch such as 4.7, 4.8 or 4.7-rc \(on '($branch)')."}
    }
    if (git status --porcelain | is-not-empty) {
        error make {msg: 'Commit or stash pending changes, including submodule changes, before releasing.'}
    }

    let remote = (remote-refs $branch)
    let target = if $line == null { null } else { line-target $line $remote }
    let props = (open --raw Directory.Build.props)
    let props = if $target == null { $props } else { $props | set-property NativesRevision $target.natives }
    let pinned = if $target == null { git rev-parse HEAD:godot } else { $target.commit }
    let godot = if $target == null { godot-version (godot-version-py $pinned) } else {
        godot-version (godot-version-py $pinned --other-line)
    }
    let version = $godot.release
    if ($named | is-not-empty) and not ($"($version)." | str starts-with $"($named.0.numbers).") {
        error make {msg: $"Branch ($branch) releases Godot ($named.0.numbers), but the godot submodule is Godot ($godot.csharp)."}
    }
    if $target != null and not ($"($version)." | str starts-with $"($line).") {
        error make {msg: $"godot-lines.json pins line ($line) to a commit of Godot ($godot.csharp)."}
    }
    let label = if $godot.status != 'stable' { $godot.status } else if ($named | is-empty) { '' } else { $named.0.label | default '' }
    let current = (property $props TwoDogRevision | into int)
    let natives = (property $props NativesRevision | into int)

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
        let built_inputs = (^uv run --no-project python scripts/native_inputs.py --kind package --ref $sha | complete)
        let current_inputs = (^uv run --no-project python scripts/native_inputs.py --kind package --godot $pinned | complete)
        if $built_inputs.exit_code != 0 or $current_inputs.exit_code != 0 {
            error make {msg: $"Cannot identify native inputs: ($built_inputs.stderr)($current_inputs.stderr)"}
        }
        ($built_inputs.stdout | str trim) == ($current_inputs.stdout | str trim)
    }
    let next_natives = if $reuse { $natives } else { $next }

    let retargeted = (retargeted-files $godot)
    let was = (property $props GodotVersion)
    print ($"Release ($tag): TwoDogRevision ($current) -> ($next), NativesRevision ($natives) -> ($next_natives)"
        + (if $was == $godot.csharp { '' } else { $", GodotVersion ($was) -> ($godot.csharp)" })
        + (if ($retargeted | is-empty) { '' } else { $", Godot ($godot.csharp) in ($retargeted | str join ', ')" }))
    let updated = ($props
        | set-property GodotVersion $godot.csharp
        | set-property TwoDogRevision $"($next)"
        | set-property NativesRevision $"($next_natives)"
        | set-property TwoDogPrerelease $label)
    # Old releases must keep resolving their godot commit after the fork's line branch (2dog-4.7) moves on.
    let fork_tag = [-C godot push (git config --file .gitmodules submodule.godot.url) $"($pinned):refs/tags/2dog-($tag)"]
    # Another line's release commit stays off the branch: only its tag is pushed.
    let push = if $target == null {
        [push --atomic origin $"HEAD:refs/heads/($branch)" $"refs/tags/($tag):refs/tags/($tag)"]
    } else {
        [push origin $"refs/tags/($tag):refs/tags/($tag)"]
    }
    if $dry_run {
        print ($"Would tag the fork's godot ($pinned) as 2dog-($tag), commit the version bump, tag ($tag), and "
            + (if $target == null { $"atomically push ($branch) and the tag to origin." } else { $"push the tag to origin; ($branch) stays as it is." }))
        return
    }

    let publish = {||
        if $target != null { checkout-line $target }
        if ('godot/.git' | path exists) {
            git ...$fork_tag | ignore
        } else {
            print --stderr $"warning: godot is not checked out; protect the pinned commit by hand: git ($fork_tag | str join ' ')"
        }
        $updated | save --force Directory.Build.props
        for file in $retargeted { open --raw $file | retarget $godot | save --force --raw $file }
        git add -- Directory.Build.props ...(if $target == null { [] } else { [.gitmodules] }) ...$retargeted | ignore
        let sign = if $no_sign { [] } else { [-c commit.gpgsign=true] }
        git ...$sign commit -m $"chore: release ($tag)" | print
        git tag --message $"Release ($tag)" $tag | ignore
        print $"Pushing (if $target == null { $'($branch) and ' } else { '' })($tag); the tag run tests the release and stages it on int.nugettest.org."
        try {
            git ...$push | print
        } catch {|err|
            error make {msg: $"($err.msg)\nLocal commit and tag remain. After resolving the push failure, retry:\ngit ($push | str join ' ')"}
        }
    }
    if $target == null { do $publish } else { detached $branch $publish }
    print $"Follow CI: https://github.com/outfox/2dog/actions?query=branch%3A($tag)"
    print $"Promote to nuget.org afterwards: gh workflow run publish.yml -f tag=($tag) -f feed=nuget"
}
