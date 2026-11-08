"""Exercise the real Nushell release script with Git replaced by a recording stub."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

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
        cat-file => { if ($env.MISSING_OBJECT? | default '') != '' { exit 1 } }
        diff => { if ($env.NATIVES_CHANGED? | default '') != '' { exit 1 } }
        merge-base => {}
        fetch => {}
        add => {}
        '-c' => {}
        commit => {}
        push => {}
        _ => { print --stderr $"Unexpected git call: ($command)"; exit 1 }
    }
}
'''
INPUTS = ["godot", "platforms", "nuget", "build", "scripts/stage_natives_release.py"]


@unittest.skipUnless(NU, "Nushell is required")
class ReleaseScript(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="2dog-release-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "scripts").mkdir()
        shutil.copyfile(REPO / "scripts/release.nu", self.root / "scripts/release.nu")
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
        self.log = self.root / "git.jsonl"
        self.env = {**os.environ, "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"], "GIT_LOG": str(self.log),
                    "REMOTE_TAGS": REMOTE_TAGS}

    def run_release(self, *args, **env):
        return subprocess.run([NU, "--no-config-file", str(self.root / "scripts/release.nu"), *args],
                              env={**self.env, **env}, capture_output=True, text=True, timeout=60)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def props(self):
        return (self.root / "Directory.Build.props").read_text()

    def test_release_takes_next_number_across_lines_and_reuses_unchanged_natives(self):
        result = self.run_release()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<TwoDogRevision>141</TwoDogRevision>", self.props())
        self.assertIn("<NativesRevision>46</NativesRevision>", self.props())
        self.assertIn("<TwoDogPrerelease></TwoDogPrerelease>", self.props())
        self.assertIn(["diff", "--quiet", "nat46", "HEAD", "--", *INPUTS], self.calls())
        self.assertEqual([
            ["add", "--", "Directory.Build.props"],
            ["-c", "commit.gpgsign=true", "commit", "-m", "chore: release v4.7.2.141"],
            ["tag", "v4.7.2.141"],
            ["push", "--atomic", "origin", "HEAD:refs/heads/main", "refs/tags/v4.7.2.141:refs/tags/v4.7.2.141"],
        ], self.calls()[-4:])

    def test_changed_native_inputs_take_the_release_number(self):
        result = self.run_release(NATIVES_CHANGED="1", MISSING_OBJECT="1")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<NativesRevision>141</NativesRevision>", self.props())
        self.assertIn(["fetch", "--quiet", "--no-tags", "origin", "nat46"], self.calls())

    def test_missing_natives_release_takes_the_release_number(self):
        result = self.run_release(REMOTE_TAGS="rel134\trefs/tags/v4.7.2.134")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<TwoDogRevision>135</TwoDogRevision>", self.props())
        self.assertIn("<NativesRevision>135</NativesRevision>", self.props())

    def test_annotated_natives_tag_compares_its_commit(self):
        tags = REMOTE_TAGS.replace("nat46\trefs/tags/v4.7.2.46",
                                   "tag-object\trefs/tags/v4.7.2.46\nnat46\trefs/tags/v4.7.2.46^{}")
        result = self.run_release(REMOTE_TAGS=tags)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(["diff", "--quiet", "nat46", "HEAD", "--", *INPUTS], self.calls())

    def test_labeled_branch_releases_a_prerelease(self):
        result = self.run_release("--no-sign", TEST_BRANCH="4.7-beta")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<TwoDogPrerelease>beta</TwoDogPrerelease>", self.props())
        self.assertEqual([
            ["commit", "-m", "chore: release v4.7.2.141-beta"],
            ["tag", "v4.7.2.141-beta"],
            ["push", "--atomic", "origin", "HEAD:refs/heads/4.7-beta",
             "refs/tags/v4.7.2.141-beta:refs/tags/v4.7.2.141-beta"],
        ], self.calls()[-3:])

    def test_stable_branch_clears_the_label(self):
        (self.root / "Directory.Build.props").write_text(PROPS.replace(
            "<TwoDogPrerelease></TwoDogPrerelease>", "<TwoDogPrerelease>rc</TwoDogPrerelease>"))
        result = self.run_release(TEST_BRANCH="4.7.2")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<TwoDogPrerelease></TwoDogPrerelease>", self.props())
        self.assertEqual(["tag", "v4.7.2.141"], self.calls()[-2])

    def test_dry_run_does_not_mutate(self):
        result = self.run_release("--dry-run", NATIVES_CHANGED="1")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("NativesRevision 46 -> 141", result.stdout)
        self.assertEqual(PROPS, self.props())
        self.assertFalse(any(call[0] in ("add", "-c", "commit", "push", "tag") and call[1] != "--list"
                             for call in self.calls()))

    def test_preflight_failures_never_mutate(self):
        for env in [
            {"TEST_BRANCH": "topic"},
            {"TEST_BRANCH": "4.8.0-beta"},  # GodotVersion 4.7.2 is not on this line
            {"TEST_BRANCH": "4.7-2"},
            {"TEST_STATUS": " M source.cs"},
            {"LOCAL_TAG": "v4.7.2.141"},
            {"FAIL_GIT": "ls-remote origin refs/heads/main refs/tags/v*"},
            {"FAIL_GIT": "merge-base --is-ancestor remote-head HEAD"},
        ]:
            with self.subTest(env=env):
                self.log.unlink(missing_ok=True)
                result = self.run_release(**env)
                self.assertNotEqual(0, result.returncode, result.stdout)
                self.assertEqual(PROPS, self.props())
                self.assertFalse(self.log.exists() and any(call[0] in ("add", "-c", "commit", "push")
                                                           for call in self.calls()))

    def test_signing_failure_never_tags_or_pushes(self):
        result = self.run_release(FAIL_GIT="-c commit.gpgsign=true commit -m chore: release v4.7.2.141")
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(any(call[0] == "push" or call == ["tag", "v4.7.2.141"] for call in self.calls()))

    def test_push_failure_keeps_recovery_command(self):
        result = self.run_release(
            FAIL_GIT="push --atomic origin HEAD:refs/heads/main refs/tags/v4.7.2.141:refs/tags/v4.7.2.141")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Local commit and tag remain", result.stderr)
        self.assertIn("git push --atomic origin HEAD:refs/heads/main", result.stderr)


if __name__ == "__main__":
    unittest.main()
