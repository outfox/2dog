"""Exercise the real Nushell release script with Git replaced by a recording stub."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

from scripts.native_inputs import BUILD_INPUTS, PACKAGE_INPUTS

REPO = Path(__file__).resolve().parents[2]
NU = shutil.which("nu")
PROPS = """<Project>
  <GodotVersion>4.7.2</GodotVersion>
  <TwoDogRevision>134</TwoDogRevision>
  <NativesRevision>46</NativesRevision>
  <TwoDogPrerelease></TwoDogPrerelease>
</Project>
"""
# Release 134 on this line, natives built by release 46, and a newer release (140) on another line.
REMOTE_TAGS = "\n".join([
    "rel134\trefs/tags/v4.7.2.134",
    "nat46\trefs/tags/v4.7.2.46",
    "beta140\trefs/tags/v4.8.0.140-beta",
    "old\trefs/tags/v0.1.21-pre",
])
FAKE_GIT = r'''
def --wrapped main [...args: string] {
    (($args | to json --raw) + "\n") | save --append $env.GIT_LOG
    let command = ($args | str join ' ')
    if $command == ($env.FAIL_GIT? | default '') { print --stderr 'simulated failure'; exit 1 }
    match $args.0 {
        branch => { print ($env.TEST_BRANCH? | default main) }
        status => { print ($env.TEST_STATUS? | default '') }
        ls-remote => {
            print $"remote-head\trefs/heads/($env.TEST_BRANCH? | default main)"
            print $env.REMOTE_TAGS
        }
        tag => { if ($args | get 1) == '--list' { print ($env.LOCAL_TAG? | default '') } }
        cat-file => {
            if $args.1 == '-e' and ($env.MISSING_OBJECT? | default '') != '' { exit 1 }
            if $args.1 == '-t' { print (if ($args.2 == 'tag-object' or $args.2 == ($env.TAG_OBJECT? | default '')) {
                'tag'
            } else { 'commit' }) }
            if $args.1 == '-p' {
                print $"object remote-head\ntype commit\ntag ($env.TAG_NAME? | default legacy)\n\n($env.TAG_INFO? | default '')"
            }
        }
        '-c' => {}
        ls-tree => {
            if ($env.NATIVES_CHANGED? | default '') != '' and $args.3 != 'HEAD' { print 'old-tree' } else { print 'same-tree' }
        }
        grep => { print ($env.SDK_FILES? | default '') }
        rev-parse => {
            # A release tag's godot gitlink (<sha>:godot); everything else resolves to the tree's pin.
            let tagged = (($args | last) | str ends-with ':godot') and ($args | last) not-in ['HEAD:godot' ':godot']
            print (if $args.1 == 'HEAD' { $env.TEST_HEAD? | default 'remote-head' } else if ($args.1 | str starts-with 'refs/tags/') {
                ($env.TAG_SOURCE? | default 'remote-head')
            } else if $tagged { $env.TAG_GODOT? | default 'pinned-godot' } else { 'pinned-godot' })
        }
        config => { print 'git@example.com:fork/godot.git' }
        '-C' => { if 'show' in $args { print ($env.LINE_VERSION_PY? | default '') } }
        show => {
            if (($args | last) | str ends-with ':pyproject.toml') {
                print '[tool.poe.tasks.build-godot]'
                print 'cmd = "python build-godot.py"'
            } else {
                print ($env.TEST_PROPS | str replace --regex '<NativesRevision>[^<]*</NativesRevision>'
                    ($env.LINE_TAG_PROPS? | default '<NativesRevision>46</NativesRevision>'))
            }
        }
        update-index => {}
        checkout => {}
        submodule => {}
        workflow => {}
        fetch => {}
        add => {}
        commit => {}
        push => {}
        _ => { print --stderr $"Unexpected git call: ($command)"; exit 1 }
    }
}
'''
INPUTS = [*BUILD_INPUTS, *PACKAGE_INPUTS]
# main carries the 4.8 line; godot-lines.json adds 4.7, last released as v4.7.2.139 with natives 120.
LINES = '{ "4.7": { "branch": "2dog-4.7", "commit": "line-commit" } }\n'
LINE_ENV = {
    "LINE_VERSION_PY": 'major = 4\nminor = 7\npatch = 2\nstatus = "stable"\n',
    "LINE_TAG_PROPS": "<NativesRevision>120</NativesRevision>",
    "REMOTE_TAGS": REMOTE_TAGS + "\nline139\trefs/tags/v4.7.2.139",
}


@unittest.skipUnless(NU, "Nushell is required")
class ReleaseScript(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="2dog-release-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "scripts").mkdir()
        shutil.copyfile(REPO / "scripts/release.nu", self.root / "scripts/release.nu")
        shutil.copyfile(REPO / "scripts/native_inputs.py", self.root / "scripts/native_inputs.py")
        (self.root / "Directory.Build.props").write_text(PROPS)
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        (bin_dir / "fake-git.nu").write_text(FAKE_GIT)
        if os.name == "nt":
            (bin_dir / "git.cmd").write_text(f'@"{NU}" --no-config-file "%~dp0fake-git.nu" %*\n')
        else:
            launcher = bin_dir / "git"
            launcher.write_text(f'#!/bin/sh\nexec "{NU}" --no-config-file "{bin_dir}/fake-git.nu" "$@"\n')
            launcher.chmod(0o755)
        git_launcher = bin_dir / ("git.cmd" if os.name == "nt" else "git")
        shutil.copy2(git_launcher, git_launcher.with_name("gh.cmd" if os.name == "nt" else "gh"))
        self.log = self.root / "git.jsonl"
        self.env = {**os.environ, "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"], "GIT_LOG": str(self.log),
                    "REMOTE_TAGS": REMOTE_TAGS, "TEST_PROPS": PROPS}
        self.write_version_py("4.7.2", "stable")
        (self.root / "godot-lines.json").write_text("{}")
        (self.root / "godot/.git").write_text("gitdir: ../.git/modules/godot\n")

    def write_version_py(self, version, status):
        (self.root / "godot").mkdir(exist_ok=True)
        major, minor, patch = version.split(".")
        (self.root / "godot/version.py").write_text(
            f'short_name = "godot"\nmajor = {major}\nminor = {minor}\npatch = {patch}\nstatus = "{status}"\n')

    def run_release(self, *args, **env):
        return subprocess.run([NU, "--no-config-file", str(self.root / "scripts/release.nu"), *args],
                              env={**self.env, **env}, capture_output=True, text=True, timeout=60)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def main_tree_with_another_line(self):
        """The tree of main: Godot 4.8 beta3 checked in, 4.7 in godot-lines.json, a game pinned to the 4.8 SDK."""
        self.write_version_py("4.8.0", "beta3")
        (self.root / "godot/.git").write_text("gitdir: ../.git/modules/godot\n")
        (self.root / "Directory.Build.props").write_text(
            PROPS.replace("<GodotVersion>4.7.2<", "<GodotVersion>4.8.0-beta.3<")
            .replace("<TwoDogPrerelease><", "<TwoDogPrerelease>beta3<"))
        (self.root / "godot-lines.json").write_text(LINES)
        (self.root / ".gitmodules").write_bytes(b'[submodule "godot"]\r\n\tpath = godot\r\n\tbranch = 2dog-4.8\r\n')
        (self.root / "game.csproj").write_text('<Project Sdk="Godot.NET.Sdk/4.8.0-beta.3" />')
        (self.root / "project.godot").write_text('config/features=PackedStringArray("4.8", "C#")\n')
        return {**LINE_ENV, "SDK_FILES": "game.csproj\nproject.godot"}

    def assert_tree_is_the_other_line(self):
        self.assertIn("<GodotVersion>4.7.2</GodotVersion>", self.props())
        self.assertIn("<TwoDogPrerelease></TwoDogPrerelease>", self.props())
        self.assertEqual(b'[submodule "godot"]\r\n\tpath = godot\r\n\tbranch = 2dog-4.7\r\n',
                         (self.root / ".gitmodules").read_bytes())
        self.assertEqual('<Project Sdk="Godot.NET.Sdk/4.7.2" />', (self.root / "game.csproj").read_text())
        self.assertEqual('config/features=PackedStringArray("4.7", "C#")\n', (self.root / "project.godot").read_text())
        self.assertIn(["update-index", "--cacheinfo", "160000,line-commit,godot"], self.calls())
        self.assertIn(["-C", "godot", "checkout", "--quiet", "--detach", "line-commit"], self.calls())

    def props(self):
        return (self.root / "Directory.Build.props").read_text()

    def test_pair_signs_tags_on_the_same_source_and_pushes_them_together(self):
        env = self.main_tree_with_another_line()
        before = self.props()
        result = self.run_release(**env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(before, self.props())
        calls = self.calls()
        self.assertEqual(1, sum(call[0] == "ls-remote" for call in calls))
        self.assertEqual([
            ["tag", "-s", "--message", "2dog-release-v1 pinned-godot 141", "v4.8.0.141-beta3", "remote-head"],
            ["tag", "-s", "--message", "2dog-release-v1 line-commit 141", "v4.7.2.141", "remote-head"],
            ["-C", "godot", "push", "--atomic", "git@example.com:fork/godot.git",
             "pinned-godot:refs/tags/2dog-pin-pinned-godot", "line-commit:refs/tags/2dog-pin-line-commit"],
            ["push", "--atomic", "origin",
             "refs/tags/v4.8.0.141-beta3:refs/tags/v4.8.0.141-beta3", "refs/tags/v4.7.2.141:refs/tags/v4.7.2.141"],
        ], [call for call in calls if call[:2] == ["tag", "-s"] or call[0] == "push"
            or call[:3] == ["-C", "godot", "push"]])
        self.assertFalse(any(call[0] in ("add", "checkout", "commit", "update-index", "workflow") for call in calls))

    def test_single_secondary_line_reuses_legacy_native_packages(self):
        env = self.main_tree_with_another_line()
        env["REMOTE_TAGS"] += "\nnat120\trefs/tags/v4.7.2.120"
        result = self.run_release("--lines", " 4.7,4.7 ", TAG_GODOT="line-commit", **env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(["ls-tree", "-r", "--full-tree", "nat120", "--", *INPUTS], self.calls())
        self.assertIn(["tag", "-s", "--message", "2dog-release-v1 line-commit 120", "v4.7.2.141", "remote-head"],
                      self.calls())
        self.assertEqual(["push", "--atomic", "origin", "refs/tags/v4.7.2.141:refs/tags/v4.7.2.141"], self.calls()[-1])

    def test_changed_native_inputs_take_next_revision(self):
        result = self.run_release(NATIVES_CHANGED="1", MISSING_OBJECT="1")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("v4.7.2.141: NativesRevision 46 -> 141", result.stdout)
        self.assertIn(["fetch", "--quiet", "--no-tags", "origin", "nat46"], self.calls())

    def test_pair_previews_both_tags_without_mutation(self):
        env = self.main_tree_with_another_line()
        before = self.props()
        result = self.run_release("--dry-run", **env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Release v4.8.0.141-beta3:", result.stdout)
        self.assertIn("Release v4.7.2.141:", result.stdout)
        self.assertEqual(before, self.props())
        self.assert_no_push()
        self.assertFalse(any(call[0] in ("checkout", "commit") or call[:2] == ["tag", "-s"] for call in self.calls()))

    def assert_no_push(self):
        self.assertFalse(any(call[0] == "push" or call[:3] == ["-C", "godot", "push"] for call in self.calls()))

    def test_preflight_failure_blocks_all_selected_lines(self):
        env = self.main_tree_with_another_line()
        for args, overrides in [
            (["--lines", "4.8,4.6"], {}),
            ([], {"TEST_STATUS": " M source.cs"}),
            ([], {"TEST_HEAD": "stale-head"}),
            ([], {"LOCAL_TAG": "v4.8.0.141-beta3"}),
            ([], {"LINE_VERSION_PY": 'major = 4\nminor = 8\npatch = 0\nstatus = "stable"\n'}),
            ([], {"FAIL_GIT": "ls-remote origin refs/heads/main refs/tags/v*"}),
        ]:
            with self.subTest(args=args, overrides=overrides):
                self.log.unlink(missing_ok=True)
                result = self.run_release(*args, **{**env, **overrides})
                self.assertNotEqual(0, result.returncode)
                self.assert_no_push()
                self.assertFalse(any(call[0] == "commit" or call[:2] == ["tag", "-s"] for call in self.calls()))

    def test_failed_second_signature_leaves_no_remote_tags(self):
        env = self.main_tree_with_another_line()
        result = self.run_release(
            FAIL_GIT="tag -s --message 2dog-release-v1 line-commit 141 v4.7.2.141 remote-head", **env)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("simulated failure", result.stderr)
        self.assert_no_push()

    def test_fork_failure_reports_retry_and_blocks_the_release_tags(self):
        result = self.run_release(FAIL_GIT="-C godot push --atomic git@example.com:fork/godot.git "
                                   "pinned-godot:refs/tags/2dog-pin-pinned-godot")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("The signed tags remain local.", result.stderr)
        self.assertIn("git push --atomic origin refs/tags/v4.7.2.141:refs/tags/v4.7.2.141", result.stderr)
        self.assertFalse(any(call[0] == "push" for call in self.calls()))

    def test_applies_verified_stable_tag_to_the_alternate_line_without_a_commit(self):
        env = self.main_tree_with_another_line()
        tag, pin = "v4.7.2.141", "a" * 40
        (self.root / "godot-lines.json").write_text(LINES.replace("line-commit", pin))
        result = self.run_release("--apply-tag", tag, TAG_NAME=tag, TAG_OBJECT=f"refs/tags/{tag}",
                                  TAG_INFO=f"2dog-release-v1 {pin} 120", **env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<GodotVersion>4.7.2</GodotVersion>", self.props())
        self.assertIn("<TwoDogRevision>141</TwoDogRevision>", self.props())
        self.assertIn("<NativesRevision>120</NativesRevision>", self.props())
        self.assertIn("<TwoDogPrerelease></TwoDogPrerelease>", self.props())
        self.assertIn(["update-index", "--cacheinfo", f"160000,{pin},godot"], self.calls())
        self.assertFalse(any(call[0] in ("commit", "push") for call in self.calls()))

    def test_sync_aligns_versions_with_a_bumped_fork_without_committing(self):
        self.write_version_py("4.8.0", "beta3")
        (self.root / "game.csproj").write_bytes(b'<Project Sdk="Godot.NET.Sdk/4.8.0-beta.2">\r\n</Project>\r\n')
        result = self.run_release("--sync", TEST_BRANCH="4.8.0-beta", TEST_STATUS=" M godot", SDK_FILES="game.csproj")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<GodotVersion>4.8.0-beta.3</GodotVersion>", self.props())
        self.assertIn("<TwoDogPrerelease>beta3</TwoDogPrerelease>", self.props())
        self.assertIn("<TwoDogRevision>134</TwoDogRevision>", self.props())
        self.assertEqual(b'<Project Sdk="Godot.NET.Sdk/4.8.0-beta.3">\r\n</Project>\r\n',
                         (self.root / "game.csproj").read_bytes())
        self.assertFalse([call for call in self.calls() if call[0] in ("add", "-c", "commit", "tag", "push")])

    def test_sync_dry_run_previews_without_writing(self):
        self.write_version_py("4.8.0", "beta3")
        (self.root / "game.csproj").write_text('<Project Sdk="Godot.NET.Sdk/4.8.0-beta.2" />')
        result = self.run_release("--sync", "--dry-run", TEST_BRANCH="4.8.0-beta", SDK_FILES="game.csproj")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Would set GodotVersion 4.8.0-beta.3", result.stdout)
        self.assertEqual(PROPS, self.props())
        self.assertEqual('<Project Sdk="Godot.NET.Sdk/4.8.0-beta.2" />', (self.root / "game.csproj").read_text())

    def test_sync_off_a_release_branch_keeps_the_committed_label_of_a_stable_godot(self):
        (self.root / "Directory.Build.props").write_text(PROPS.replace("<TwoDogPrerelease></TwoDogPrerelease>",
                                                                       "<TwoDogPrerelease>rc</TwoDogPrerelease>"))
        result = self.run_release("--sync", TEST_BRANCH="")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<TwoDogPrerelease>rc</TwoDogPrerelease>", self.props())
        self.assertIn("<GodotVersion>4.7.2</GodotVersion>", self.props())

    def test_sync_line_switches_the_tree_to_another_line_without_committing(self):
        env = self.main_tree_with_another_line()
        result = self.run_release("--sync", "--line", "4.7", **env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assert_tree_is_the_other_line()
        # The natives of the line's latest release (v4.7.2.139), not the tree's.
        self.assertIn("<NativesRevision>120</NativesRevision>", self.props())
        self.assertIn(["show", "line139:Directory.Build.props"], self.calls())
        self.assertIn("<TwoDogRevision>134</TwoDogRevision>", self.props())
        self.assertFalse([call for call in self.calls() if call[0] in ("add", "-c", "commit", "tag", "push")])

    def test_ci_line_pushes_the_line_as_a_branch_and_returns_to_the_start(self):
        env = self.main_tree_with_another_line()
        result = self.run_release("--ci", "--line", "4.7", **env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assert_tree_is_the_other_line()
        self.assertIn(["checkout", "--quiet", "--detach"], self.calls())
        self.assertEqual([
            ["add", "--", "Directory.Build.props", ".gitmodules", "game.csproj", "project.godot"],
            ["commit", "--quiet", "-m", "ci: Godot 4.7 line of pinned-godot"],
            ["push", "origin", "HEAD:refs/heads/ci/4.7/pinned-godot"],
            ["checkout", "--quiet", "--force", "main"],
            ["submodule", "update", "--quiet", "godot"],
        ], self.calls()[-5:])

    def test_unnumbered_godot_prerelease_is_refused(self):
        for status, args in [("beta", []), ("beta.2", []), ("beta", ["--sync"])]:
            with self.subTest(status=status, args=args):
                self.write_version_py("4.8.0", status)
                result = self.run_release(*args)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("nuget.org has only numbered Godot prereleases", result.stderr)
                self.assertEqual(PROPS, self.props())


if __name__ == "__main__":
    unittest.main()
