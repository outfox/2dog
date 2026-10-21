"""The scoped feed must stay complete and a sticky cache must never select another run's natives."""
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from scripts import stage_android_ci as ci


def package(folder, name, dependencies=(), contents=None):
    path = folder / f'{name}.1.0.0.nupkg'
    with zipfile.ZipFile(path, 'w') as archive:
        deps = ''.join(f'<dependency id="{name}" version="[1.0.0]" />' for name in dependencies)
        archive.writestr('package.nuspec', '<package><metadata>'
                         f'<id>{name}</id><version>1.0.0</version><dependencies>{deps}</dependencies></metadata></package>')
        for name, data in (contents or {}).items():
            archive.writestr(name, data)
    return path


class AndroidInputsTests(unittest.TestCase):
    def fixture(self, root, precision='single'):
        feed, sdk, output = (root / name for name in ('feed', 'sdk', 'output'))
        feed.mkdir()
        sdk.mkdir()
        suffix = '.double' if precision == 'double' else ''
        package(feed, f'2dog.engine{suffix}', (f'2dog.linux-x64{suffix}', f'2dog.godotsharp{suffix}'))
        package(feed, f'2dog.linux-x64{suffix}', (f'2dog.linux-x64.editor{suffix}',))
        for name in (f'2dog.linux-x64.editor{suffix}', f'2dog.godotsharp{suffix}', f'2dog.godotsharp.editor{suffix}', '2dog.android'):
            package(feed, name)
        for rid in ('android-x64', 'android-arm64'):
            package(feed, f'2dog.{rid}{suffix}', (f'2dog.{rid}.release{suffix}',))
            package(feed, f'2dog.{rid}.release{suffix}', contents={
                f'runtimes/{rid}/native/libgodot_android.so': f'{precision}:{rid}'.encode(),
                f'runtimes/{rid}/native/libc++_shared.so': b'C++ runtime'})
        package(feed, '2dog.browser-wasm')
        package(feed, '2dog.avalonia')
        package(sdk, 'Godot.NET.Sdk')
        for variant in ('template_debug', 'template_release'):
            for rid, arch in (('android-x64', 'x86_64'), ('android-arm64', 'arm64')):
                native = root / f'natives/{variant}/{arch}'
                native.mkdir(parents=True)
                (native / 'libgodot_android.so').write_bytes(f'{precision}:{rid}'.encode())
                (native / 'libc++_shared.so').write_bytes(b'C++ runtime')
        return feed, sdk, output

    def test_dependency_closure_preserves_desktop_transitives_and_omits_unused_packages(self):
        for precision in ('single', 'double'):
            with self.subTest(precision=precision), tempfile.TemporaryDirectory() as directory:
                feed, sdk, output = self.fixture(Path(directory), precision)
                ci.stage(feed, sdk, output, precision, 'run:sha', Path(directory) / 'natives')
                names = {ci.package(path)[0] for path in (output / 'packages').glob('*.nupkg')}
                suffix = '.double' if precision == 'double' else ''
                self.assertIn(f'2dog.linux-x64.editor{suffix}', names)
                self.assertNotIn('2dog.browser-wasm', names)
                self.assertNotIn('2dog.avalonia', names)
                self.assertTrue(ci.matches(output, precision, 'run:sha'))
                self.assertFalse(ci.matches(output, precision, 'other-run:sha'))
                self.assertFalse(ci.matches(output, 'double' if precision == 'single' else 'single', 'run:sha'))

    def test_missing_dependency_fails_before_replacing_a_good_input_set(self):
        with tempfile.TemporaryDirectory() as directory:
            feed, sdk, output = self.fixture(Path(directory))
            ci.stage(feed, sdk, output, 'single', 'good', Path(directory) / 'natives')
            (feed / '2dog.linux-x64.editor.1.0.0.nupkg').unlink()
            with self.assertRaisesRegex(ValueError, 'Missing Android dependency'):
                ci.stage(feed, sdk, output, 'single', 'bad', Path(directory) / 'natives')
            self.assertTrue(ci.matches(output, 'single', 'good'))

    def test_stale_cache_reset_and_partial_or_escaping_manifests_are_misses(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            feed, sdk, output = self.fixture(root)
            ci.stage(feed, sdk, output, 'single', 'good', root / 'natives')
            manifest = json.loads((output / 'manifest.json').read_text())
            (output / manifest['files'][0]).unlink()
            self.assertFalse(ci.matches(output, 'single', 'good'))
            (root / 'outside').touch()
            manifest['files'] = ['../outside']
            (output / 'manifest.json').write_text(json.dumps(manifest))
            self.assertFalse(ci.matches(output, 'single', 'good'))
            ci.reset(output)
            self.assertFalse(ci.matches(output, 'single', 'good'))
            self.assertFalse((output / 'packages').exists())
            self.assertTrue((root / 'outside').is_file())
            with self.assertRaisesRegex(ValueError, 'Unsafe cache directory'):
                ci.clear_child(output, root)

    def test_prepare_uses_exact_precision_and_abi_and_invalidates_mutable_packages_only(self):
        for precision, subdir in (('single', ''), ('double', 'double/')):
            with self.subTest(precision=precision), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                feed, sdk, output = self.fixture(root, precision)
                ci.stage(feed, sdk, output, precision, 'run', root / 'natives')
                workspace = root / 'work'
                cache = workspace / '.packages'
                for name in ('2dog.engine', 'godot.net.sdk', 'microsoft.android.runtime'):
                    (cache / name).mkdir(parents=True)
                    (cache / name / 'old').touch()
                ci.prepare(output, workspace, precision, 'android-arm64', 'Release')
                native = workspace / f'godot/bin/{subdir}android/template_release/arm64'
                self.assertEqual(f'{precision}:android-arm64'.encode(), (native / 'libgodot_android.so').read_bytes())
                self.assertEqual(b'C++ runtime', (native / 'libc++_shared.so').read_bytes())
                self.assertFalse((cache / '2dog.engine').exists())
                self.assertFalse((cache / 'godot.net.sdk').exists())
                self.assertTrue((cache / 'microsoft.android.runtime/old').is_file())
                self.assertTrue(list((workspace / 'godot/bin/GodotSharp/Tools/nupkgs').glob('*.nupkg')))

    def test_package_with_wrong_native_bytes_is_rejected_against_original_build_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            feed, sdk, output = self.fixture(root)
            ci.stage(feed, sdk, output, 'single', 'run', root / 'natives')
            package(output / 'packages', '2dog.android-x64.release', contents={
                'runtimes/android-x64/native/libgodot_android.so': b'wrong variant',
                'runtimes/android-x64/native/libc++_shared.so': b'C++ runtime'})
            with self.assertRaisesRegex(ValueError, 'differs from the original native build'):
                ci.prepare(output, root / 'work', 'single', 'android-x64', 'Release')


if __name__ == '__main__':
    unittest.main()
