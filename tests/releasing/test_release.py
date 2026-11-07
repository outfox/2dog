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
</Project>
"""
FAKE_GIT = r'''
def --wrapped main [...args: string] {
    (($args | to json --raw) + "\n") | save --append $env.GIT_LOG
    let command = ($args | str join ' ')
    if $command == ($env.FAIL_GIT? | default '') { print --stderr 'simulated failure'; exit 1 }
    match $args.0 {
        branch => { print ($env.TEST_BRANCH? | default '4.7') }
        status => { print ($env.TEST_STATUS? | default '') }
        ls-remote => {
            print $"remote-head\t($args.2)"
            for tag in ($env.TAGS? | default '' | split row ',' | where $it != '') { print $"tag-sha\trefs/tags/($tag)" }
        }
        tag => { if ($args | get 1) == '--list' { print ($env.LOCAL_TAG? | default '') } }
        merge-base => {}
        add => {}
        '-c' => {}
        push => {}
        _ => { print --stderr $"Unexpected git call: ($command)"; exit 1 }
    }
}
'''


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
        self.env = {**os.environ, "PATH": str(bin_dir) + os.pathsep + os.environ["PATH"], "GIT_LOG": str(self.log)}

    def write_props(self, version="4.7.2"):
        (self.root / "Directory.Build.props").write_text(PROPS.replace("4.7.2", version))

    def run_release(self, *args, **env):
        return subprocess.run([NU, "--no-config-file", str(self.root / "scripts/release.nu"), *args],
                              env={**self.env, **env}, capture_output=True, text=True, timeout=60)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def test_native_release_signs_then_tags_then_atomically_pushes(self):
        result = self.run_release("--native")
        self.assertEqual(0, result.returncode, result.stderr)
        props = (self.root / "Directory.Build.props").read_text()
        self.assertIn("<TwoDogRevision>135</TwoDogRevision>", props)
        self.assertIn("<NativesRevision>47</NativesRevision>", props)
        self.assertEqual([
            ["add", "--", "Directory.Build.props"],
            ["-c", "commit.gpgsign=true", "commit", "-m", "chore: release v4.7.2.135"],
            ["tag", "v4.7.2.135"],
            ["push", "--atomic", "origin", "HEAD:refs/heads/4.7", "refs/tags/v4.7.2.135:refs/tags/v4.7.2.135"],
        ], self.calls()[-4:])

    def test_revision_continues_after_the_highest_tag_of_any_line(self):
        # Peeled annotated-tag lines and older revisions do not count; another line's newer release does.
        result = self.run_release(TAGS="v4.7.2.134,v4.8.0-beta.1.140,v4.8.0-beta.1.140^{},v4.6.2.31")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("<TwoDogRevision>141</TwoDogRevision>", (self.root / "Directory.Build.props").read_text())
        self.assertIn(["tag", "v4.7.2.141"], self.calls())

    def test_numbered_prerelease_releases_from_its_line_branch(self):
        self.write_props("4.8.0-beta.1")
        result = self.run_release(TEST_BRANCH="4.8", TAGS="v4.7.2.150")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["push", "--atomic", "origin", "HEAD:refs/heads/4.8",
                          "refs/tags/v4.8.0-beta.1.151:refs/tags/v4.8.0-beta.1.151"], self.calls()[-1])

    def test_explicit_managed_revision_preserves_natives(self):
        result = self.run_release("--revision", "140")
        self.assertEqual(0, result.returncode, result.stderr)
        props = (self.root / "Directory.Build.props").read_text()
        self.assertIn("<TwoDogRevision>140</TwoDogRevision>", props)
        self.assertIn("<NativesRevision>46</NativesRevision>", props)

    def test_dry_run_does_not_mutate(self):
        result = self.run_release("--native", "--dry-run")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("NativesRevision 46 -> 47", result.stdout)
        self.assertEqual(PROPS, (self.root / "Directory.Build.props").read_text())
        self.assertFalse(any(call[0] in ("add", "-c", "push") or call == ["tag", "v4.7.2.135"] for call in self.calls()))

    def test_unreleasable_godot_versions_never_mutate(self):
        # An unnumbered prerelease (Godot master between betas) would sort above the numbered ones after it.
        for version, branch in [("4.8.0-beta", "4.8"), ("4.8.0-dev.3", "4.8"), ("4.8.0-beta.1", "4.7")]:
            with self.subTest(version=version, branch=branch):
                self.write_props(version)
                self.log.unlink(missing_ok=True)
                result = self.run_release(TEST_BRANCH=branch)
                self.assertNotEqual(0, result.returncode, result.stdout)
                self.assertIn(f"<GodotVersion>{version}</GodotVersion>", (self.root / "Directory.Build.props").read_text())
                self.assertFalse(self.log.exists() and any(call[0] in ("add", "-c", "push") for call in self.calls()))

    def test_preflight_failures_never_mutate(self):
        for args, env in [
            ([], {"TEST_BRANCH": "main"}),
            ([], {"TEST_BRANCH": "topic"}),
            ([], {"TEST_STATUS": " M source.cs"}),
            (["--revision", "134"], {}),
            (["--revision", "140"], {"TAGS": "v4.8.0-beta.1.140"}),
            ([], {"LOCAL_TAG": "v4.7.2.135"}),
            ([], {"FAIL_GIT": "ls-remote origin refs/heads/4.7 refs/tags/v*"}),
            ([], {"FAIL_GIT": "merge-base --is-ancestor remote-head HEAD"}),
        ]:
            with self.subTest(args=args, env=env):
                self.log.unlink(missing_ok=True)
                result = self.run_release(*args, **env)
                self.assertNotEqual(0, result.returncode, result.stdout)
                self.assertEqual(PROPS, (self.root / "Directory.Build.props").read_text())
                self.assertFalse(any(call[0] in ("add", "-c", "push") for call in self.calls()))

    def test_signing_failure_never_tags_or_pushes(self):
        result = self.run_release(FAIL_GIT="-c commit.gpgsign=true commit -m chore: release v4.7.2.135")
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(any(call[0] == "push" or call == ["tag", "v4.7.2.135"] for call in self.calls()))

    def test_push_failure_keeps_recovery_command(self):
        result = self.run_release(FAIL_GIT="push --atomic origin HEAD:refs/heads/4.7 refs/tags/v4.7.2.135:refs/tags/v4.7.2.135")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Local commit and tag remain", result.stderr)
        self.assertIn("git push --atomic origin HEAD:refs/heads/4.7", result.stderr)


if __name__ == "__main__":
    unittest.main()
