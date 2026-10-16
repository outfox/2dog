"""Check snapshot selection, feed completeness, and narrowly scoped cache refreshes."""

from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from scripts import pull_ci_packages as pull


class PullPackagesTests(unittest.TestCase):
    def test_selects_previous_available_run_when_latest_only_reuses_checks(self):
        def api(_repo, endpoint):
            if endpoint.endswith("/artifacts?per_page=100"):
                run = int(endpoint.split("/")[2])
                return {"artifacts": [] if run == 2 else [
                    {"name": "nuget-packages-single", "expired": False}]}
            return {"id": int(endpoint.split("/")[-1]), "status": "completed", "conclusion": "success"}

        with patch.object(pull, "command", return_value='[{"databaseId": 2}, {"databaseId": 1}]'), \
                patch.object(pull, "api", side_effect=api):
            run, _artifacts = pull.choose_run("outfox/2dog", "webgpu", None, ("single",))
        self.assertEqual(run["id"], 1)

    def test_rejects_expired_or_missing_precision(self):
        artifacts = [{"name": "nuget-packages-single", "expired": False},
                     {"name": "nuget-packages-double", "expired": True}]
        self.assertTrue(pull.available_artifacts(artifacts, ("single",)))
        self.assertFalse(pull.available_artifacts(artifacts, ("single", "double")))

    def test_rejects_unsuccessful_explicit_run(self):
        with patch.object(pull, "api", return_value={"id": 1, "status": "completed", "conclusion": "failure"}):
            with self.assertRaisesRegex(ValueError, "not completed successfully"):
                pull.choose_run("outfox/2dog", "webgpu", 1, ("single",))

    def test_rejects_archive_traversal_before_extracting_any_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with zipfile.ZipFile(root / "unsafe.zip", "w") as archive:
                archive.writestr("Godot.SourceGenerators.dll", b"good")
                archive.writestr("../outside.txt", b"bad")
            with self.assertRaisesRegex(ValueError, "Unsafe archive path"):
                pull.extract_checked(root / "unsafe.zip", root / "output", ["Godot.SourceGenerators.dll"])
            self.assertFalse((root / "output/Godot.SourceGenerators.dll").exists())
            self.assertFalse((root / "outside.txt").exists())

    def test_rejects_unsafe_nuspec_id(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "unsafe.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("unsafe.nuspec", '<package><metadata><id>../outside</id>'
                                 '<version>4.7.2.123</version></metadata></package>')
            with self.assertRaisesRegex(ValueError, "unsafe package ID"):
                pull.inspect_package(package)

    def test_refreshes_only_changed_package_and_version(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            feed, cache = root / "feed", root / "cache"
            feed.mkdir()
            packages = []
            for name, payload in (("2dog.engine", b"new"), ("2dog.xunit", b"identical")):
                source = feed / f"{name}.4.7.2.123.nupkg"
                source.write_bytes(payload)
                packages.append(pull.Package(source, name, "4.7.2.123", "", True))
                target = cache / name / "4.7.2.123"
                target.mkdir(parents=True)
                (target / source.name).write_bytes(b"old" if name == "2dog.engine" else payload)
            previous = cache / "2dog.engine/4.7.2.122"
            previous.mkdir()
            unrelated = cache / "unrelated/1.0.0"
            unrelated.mkdir(parents=True)
            self.assertEqual(pull.refresh_cache(cache, packages), 1)
            self.assertFalse((cache / "2dog.engine/4.7.2.123").exists())
            self.assertTrue((cache / "2dog.xunit/4.7.2.123").exists())
            self.assertTrue(previous.exists())
            self.assertTrue(unrelated.exists())

    def test_keeps_single_precision_neutral_packages_and_rejects_stubs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            entries = {}
            for precision in ("single", "double"):
                suffix = ".double" if precision == "double" else ""
                feed = root / f"nuget-packages-{precision}"
                feed.mkdir()
                ids = {f"2dog.{name}{suffix}" for name in
                       ("engine", "godotsharp", "godotsharp.editor", "tools", "browser-wasm",
                        "win-x64", "linux-x64", "osx-arm64")}
                ids |= {f"2dog.{rid}.{variant}{suffix}" for rid in ("win-x64", "linux-x64", "osx-arm64")
                        for variant in ("debug", "release", "editor")}
                ids |= {f"2dog.browser-wasm.{variant}{suffix}" for variant in ("debug", "release")}
                ids.add("2dog.avalonia")
                for package_id in ids:
                    path = feed / f"{package_id}.nupkg"
                    path.touch()
                    native = bool(pull.NATIVE_ID.match(package_id))
                    entries[path] = pull.Package(path, package_id, "4.7.2.28" if native else "4.7.2.123",
                                                 "native" if native else "managed", True)
            with patch.object(pull, "inspect_package", side_effect=entries.__getitem__):
                packages = pull.collect_packages(root, ("single", "double"),
                                                 ("4.7.2", "4.7.2.123", "4.7.2.28"), "managed", "native")
                neutral = next(p for p in packages if p.id == "2dog.avalonia")
                self.assertEqual(neutral.path.parent.name, "nuget-packages-single")
                native = next(p for p in packages if p.id == "2dog.linux-x64.release")
                entries[native.path] = pull.Package(native.path, native.id, native.version, native.commit, False)
                with self.assertRaisesRegex(ValueError, "native payload is missing"):
                    pull.collect_packages(root, ("single",),
                                          ("4.7.2", "4.7.2.123", "4.7.2.28"), "managed", "native")
                entries[native.path] = pull.Package(native.path, native.id, "4.7.2.27", native.commit, True)
                with self.assertRaisesRegex(ValueError, "version/source does not match"):
                    pull.collect_packages(root, ("single",),
                                          ("4.7.2", "4.7.2.123", "4.7.2.28"), "managed", "native")


if __name__ == "__main__":
    unittest.main()
