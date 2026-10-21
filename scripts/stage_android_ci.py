"""Keep Android CI inputs small and reject sticky inputs from another workflow run."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
import zipfile


def package(path):
    with zipfile.ZipFile(path) as archive:
        nuspec, = [name for name in archive.namelist() if name.endswith('.nuspec')]
        metadata = ET.fromstring(archive.read(nuspec)).find('{*}metadata')
        return metadata.findtext('{*}id').lower(), [
            dependency.attrib['id'].lower() for dependency in metadata.findall('.//{*}dependency')]


def clear_child(root, child):
    root, child = root.resolve(), child.absolute()
    if child.parent != root or not child.resolve().is_relative_to(root) or child.is_symlink():
        raise ValueError(f'Unsafe cache directory: {child}')
    if child.exists():
        shutil.rmtree(child)


def stage(feed, sdk, output, precision, identity, natives=None):
    feed, sdk, output = feed.resolve(), sdk.resolve(), output.resolve()
    if feed.is_relative_to(output) or sdk.is_relative_to(output):
        raise ValueError('CI output must not contain its source feeds')
    packages = {}
    for path in feed.glob('*.nupkg'):
        name, dependencies = package(path)
        if name in packages:
            raise ValueError(f'Multiple versions of {name} in CI feed')
        packages[name] = path, dependencies
    suffix = '.double' if precision == 'double' else ''
    pending = [f'2dog.engine{suffix}', f'2dog.godotsharp.editor{suffix}', '2dog.android',
               f'2dog.android-x64{suffix}', f'2dog.android-arm64{suffix}']
    selected = {}
    while pending:
        name = pending.pop()
        if name in selected:
            continue
        if name not in packages:
            raise ValueError(f'Missing Android dependency: {name}')
        path, dependencies = packages[name]
        selected[name] = path
        pending.extend(name for name in dependencies if name == '2dog' or name.startswith('2dog.'))
    sdks = list(sdk.glob('*.nupkg'))
    if not any(path.name.lower().startswith('godot.net.sdk.') for path in sdks):
        raise ValueError('Missing fork Godot.NET.Sdk package')
    natives = natives or Path('godot/bin') / ('double' if suffix else '') / 'android'
    hashes = {}
    for variant in ('template_debug', 'template_release'):
        for arch in ('x86_64', 'arm64'):
            for library in ('libgodot_android.so', 'libc++_shared.so'):
                relative = f'{variant}/{arch}/{library}'
                with (natives / relative).open('rb') as native:
                    hashes[relative] = hashlib.file_digest(native, 'sha256').hexdigest()
    output.mkdir(parents=True, exist_ok=True)
    (output / 'manifest.json').unlink(missing_ok=True)
    files = []
    for folder, paths in (('packages', selected.values()), ('sdk', sdks)):
        clear_child(output, output / folder)
        (output / folder).mkdir()
        for path in paths:
            destination = output / folder / path.name
            shutil.copyfile(path, destination)
            files.append(str(destination.relative_to(output)))
    (output / 'manifest.json').write_text(json.dumps(dict(
        identity=identity, precision=precision, files=files, native_sha256=hashes)))
    print(f'Android inputs: {len(selected)} packages, {len(sdks)} SDK packages, '
          f'{sum((output / name).stat().st_size for name in files) / 1_000_000:.1f} MB')


def matches(root, precision, identity):
    try:
        manifest = json.loads((root / 'manifest.json').read_text())
        return (manifest['identity'] == identity and manifest['precision'] == precision
                and bool(manifest.get('native_sha256'))
                and bool(manifest['files']) and all(
                    (root / name).resolve().is_relative_to(root.resolve()) and (root / name).is_file()
                    for name in manifest['files']))
    except (OSError, ValueError, KeyError, TypeError):
        return False


def reset(root):
    (root / 'manifest.json').unlink(missing_ok=True)
    for name in ('packages', 'sdk'):
        clear_child(root, root / name)


def prepare(root, workspace, precision, rid, configuration):
    root, workspace = root.resolve(), workspace.resolve()
    sdk = workspace / 'godot/bin/GodotSharp/Tools/nupkgs'
    sdk.mkdir(parents=True, exist_ok=True)
    cache = workspace / '.packages'
    sdk_ids = set()
    for path in (root / 'sdk').glob('*.nupkg'):
        sdk_ids.add(package(path)[0])
        shutil.copyfile(path, sdk / path.name)
    # Unpublished 2dog and fork SDK versions can change bytes without changing version.
    if cache.exists():
        for path in cache.iterdir():
            if path.is_dir() and (path.name == '2dog' or path.name.startswith('2dog.') or path.name in sdk_ids):
                clear_child(cache, path)
    suffix = '.double' if precision == 'double' else ''
    name = f'2dog.{rid}.{configuration.lower()}{suffix}'
    candidates = [path for path in (root / 'packages').glob('*.nupkg') if package(path)[0] == name]
    if len(candidates) != 1:
        raise ValueError(f'Expected one native package: {name}')
    arch = 'x86_64' if rid == 'android-x64' else 'arm64'
    native = workspace / 'godot/bin' / ('double' if suffix else '') / 'android' / f'template_{configuration.lower()}' / arch
    native.mkdir(parents=True, exist_ok=True)
    expected = json.loads((root / 'manifest.json').read_text())['native_sha256']
    with zipfile.ZipFile(candidates[0]) as archive:
        for library in ('libgodot_android.so', 'libc++_shared.so'):
            data = archive.read(f'runtimes/{rid}/native/{library}')
            if hashlib.sha256(data).hexdigest() != expected[f'template_{configuration.lower()}/{arch}/{library}']:
                raise ValueError(f'{name}: {library} differs from the original native build')
            (native / library).write_bytes(data)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('stage', 'check', 'prepare', 'reset'))
    parser.add_argument('--root', type=Path, default=Path('.ci/android-inputs'))
    parser.add_argument('--precision', choices=('single', 'double'), default='single')
    parser.add_argument('--identity')
    parser.add_argument('--feed', type=Path, default=Path('packages'))
    parser.add_argument('--sdk', type=Path, default=Path('godot/bin/GodotSharp/Tools/nupkgs'))
    parser.add_argument('--natives', type=Path, help='Original Android native build outputs (default: godot/bin/[double/]android)')
    parser.add_argument('--workspace', type=Path, default=Path.cwd())
    parser.add_argument('--rid', choices=('android-x64', 'android-arm64'), default='android-x64')
    parser.add_argument('--configuration', choices=('Debug', 'Release'), default='Debug')
    args = parser.parse_args()
    if args.command == 'stage':
        if not args.identity:
            parser.error('--identity is required for staging')
        stage(args.feed, args.sdk, args.root, args.precision, args.identity, args.natives)
    elif args.command == 'check':
        raise SystemExit(0 if args.identity and matches(args.root, args.precision, args.identity) else 1)
    elif args.command == 'reset':
        reset(args.root)
    else:
        prepare(args.root, args.workspace, args.precision, args.rid, args.configuration)
