#!/usr/bin/env nu
# The terminal dispatches the workflow, which calls --batch after taking the release lock.

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

def main [
    --dry-run       # Preview without changing tracked files, commits, tags or remote refs.
    --lines: string = '' # Comma-separated Godot lines to release; empty selects every configured line.
    --batch         # Prepare and publish tags in the release workflow.
    --sync          # Only align GodotVersion, TwoDogPrerelease, the Godot.NET.Sdk pins and feature tags with godot/version.py (after a fork bump). With --line: switch the working tree to that line.
    --ci            # With --line: push that line's tree to the branch ci/<line>/<commit>, which runs CI on it.
    --line: string  # Another Godot line from godot-lines.json (4.7) instead of the one in the tree.
] {
    if $line != null and not ($sync or $ci) {
        error make {msg: '--line is for --sync or --ci; use --lines to select release lines.'}
    }
    if ($batch or $lines != '') and ($sync or $ci) {
        error make {msg: '--batch and --lines are only for releases.'}
    }
    if not ($batch or $sync or $ci) {
        ^gh workflow run release.yml --repo outfox/2dog --ref main -f $"lines=($lines)" -f $"dry_run=($dry_run)"
        return
    }
    cd ($env.FILE_PWD | path dirname)
    let branch = (git branch --show-current)
    if $ci and $line == null {
        error make {msg: '--ci needs --line: CI for the line in the tree runs on the branch itself.'}
    }
    if $sync or $ci {
        let named = ($branch | parse --regex '^(?<numbers>\d+\.\d+(?:\.\d+)?)(?:-(?<label>dev|beta|rc))?$')
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
    if $branch != 'main' { error make {msg: 'The release workflow must check out main.'} }
    if (git status --porcelain | is-not-empty) {
        error make {msg: 'The release checkout must be clean, including the Godot submodule.'}
    }
    if not $dry_run and ($env.GITHUB_ACTIONS? != 'true' or $env.GITHUB_OUTPUT? == null) {
        error make {msg: 'Dispatch the release workflow with uv run --locked poe release.'}
    }
    if not ('godot/.git' | path exists) {
        error make {msg: 'Initialize the Godot submodule before releasing.'}
    }

    let remote = (remote-refs $branch)
    let source = (git rev-parse HEAD)
    if not ($remote | any {|ref| $ref.ref == 'refs/heads/main' and $ref.sha == $source }) {
        error make {msg: 'main moved before release planning. Dispatch the release workflow again.'}
    }
    let main_pin = (git rev-parse HEAD:godot)
    let main_godot = (godot-version (godot-version-py $main_pin))
    let all_lines = ([$main_godot.feature ...((open godot-lines.json) | columns)] | uniq)
    let selected = if $lines == '' { $all_lines } else { $lines | split row ',' | str trim | uniq }
    let unknown = ($selected | where {|line| $line not-in $all_lines })
    if ($unknown | is-not-empty) {
        error make {msg: $"Select Godot lines from ($all_lines | str join ', ')."}
    }
    let props = (open --raw Directory.Build.props)
    let revisions = ($remote | get ref
        | parse --regex '^refs/tags/v\d+\.\d+\.\d+\.(?<n>\d+)(?:-[0-9A-Za-z.-]+)?$'
        | get n | into int)
    let next = ([(property $props TwoDogRevision | into int) ...$revisions] | math max) + 1
    let plans = ($selected | each {|line|
        let target = if $line == $main_godot.feature { null } else { line-target $line $remote }
        let pinned = if $target == null { $main_pin } else { $target.commit }
        if $target != null and (^git -C godot cat-file -e $"($pinned)^{commit}" | complete).exit_code != 0 {
            git -C godot fetch --quiet --no-tags --depth 1 origin $pinned | ignore
        }
        let godot = if $target == null { $main_godot } else { godot-version (godot-version-py $pinned --other-line) }
        if $godot.feature != $line {
            error make {msg: $"godot-lines.json pins line ($line) to a commit of Godot ($godot.csharp)."}
        }
        let natives = (if $target == null { property $props NativesRevision } else { $target.natives } | into int)
        release-plan $target $pinned $godot $props $next $natives $remote
    })
    if $dry_run { return }

    for plan in $plans {
        detached main {||
            if $plan.target != null { checkout-line $plan.target }
            $plan.props | save --force Directory.Build.props
            for file in $plan.files { open --raw $file | retarget $plan.godot | save --force --raw $file }
            git add -- Directory.Build.props ...(if $plan.target == null { [] } else { [.gitmodules] }) ...$plan.files | ignore
            git commit -S -m $"chore: release ($plan.tag)" | print
            git tag -s --message $"Release ($plan.tag)" $plan.tag | ignore
        }
    }
    let fork = (git config --file .gitmodules submodule.godot.url)
    # A failed 2dog push can leave fork pins behind, so name them by commit for safe retries.
    let fork_refs = ($plans | get pinned | uniq | each {|pin| $"($pin):refs/tags/2dog-pin-($pin)" })
    git -C godot push --atomic $fork ...$fork_refs | print

    let tags = ($plans | get tag)
    let tag_refs = ($tags | each {|tag| $"refs/tags/($tag):refs/tags/($tag)" })
    let branch_refs = ($plans | where {|plan| $plan.target == null } | each {|plan|
        $"(git rev-parse $"($plan.tag)^{}"):refs/heads/main"
    })
    git push --atomic origin ...$branch_refs ...$tag_refs | print
    ($"tags=($tags | str join ' ')" + "\n") | save --append $env.GITHUB_OUTPUT
}

def release-plan [target: any, pinned: string, godot: record, props: string, next: int, natives: int, remote: list] {
    let version = $godot.release
    let label = if $godot.status == 'stable' { '' } else { $godot.status }
    let suffix = if $label == '' { '' } else { $"-($label)" }
    let tag = $"v($version).($next)($suffix)"
    if (git tag --list $tag | is-not-empty) or ($remote | where ref == $"refs/tags/($tag)" | is-not-empty) {
        error make {msg: $"Tag ($tag) already exists."}
    }

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

    let files = (retargeted-files $godot)
    print $"Release ($tag): NativesRevision ($natives) -> ($next_natives), Godot SDK ($godot.csharp)"
    let updated = ($props
        | set-property GodotVersion $godot.csharp
        | set-property TwoDogRevision $"($next)"
        | set-property NativesRevision $"($next_natives)"
        | set-property TwoDogPrerelease $label)
    {tag: $tag, target: $target, pinned: $pinned, godot: $godot, props: $updated, files: $files}
}
