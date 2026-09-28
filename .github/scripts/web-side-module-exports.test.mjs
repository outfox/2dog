import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test from 'node:test';

const run = promisify(execFile);
const targets = process.env.TWODOG_WEB_TARGETS
    ?? fileURLToPath(new URL('../../platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets', import.meta.url));
const xml = (s) => s.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
const leb = (n) => n < 128 ? [n] : [(n & 127) | 128, ...leb(n >>> 7)];
const str = (s) => [...leb(Buffer.byteLength(s)), ...Buffer.from(s)];
const section = (id, bytes) => [id, ...leb(bytes.length), ...bytes];
function sideModule(symbol, shared = false) {
    return Buffer.from([
        0, 97, 115, 109, 1, 0, 0, 0,
        ...section(0, [...str('dylink.0'), 1, 4, 0, 0, 0, 0]),
        ...section(1, [1, 0x60, 0, 0]),
        ...section(2, [2, ...str('env'), ...str(symbol), 0, 0,
            ...str('env'), ...str('memory'), 2, ...(shared ? [3, 1, 2] : [0, 1])]),
    ]);
}

test('referenced side modules survive .gdignore, excluding shared-memory variants and ignored hosts', { timeout: 60000 }, async () => {
    const dir = await mkdtemp(join(tmpdir(), 'twodog-side-modules-'));
    try {
        const game = join(dir, 'game');
        const libs = join(game, 'addons', 'probe', 'native libs');
        const host = join(game, 'host');
        await mkdir(libs, { recursive: true });
        await mkdir(host);
        await writeFile(join(libs, '.gdignore'), '');
        await writeFile(join(host, '.gdignore'), '');
        await writeFile(join(libs, 'release.wasm'), sideModule('hidden_cpp_function'));
        await writeFile(join(libs, 'dependency.wasm'), sideModule('hidden_dependency'));
        await writeFile(join(libs, 'threads.wasm'), sideModule('thread_only_function', true));
        await writeFile(join(libs, 'unused.wasm'), sideModule('unreferenced_function'));
        await writeFile(join(host, 'host.wasm'), sideModule('ignored_host_function'));
        await writeFile(join(host, 'host.gdextension'), '[libraries]\nweb = "host.wasm"');
        await writeFile(join(game, 'addons', 'probe', 'probe.gdextension'), `
[libraries]
web.wasm32.nothreads = "native libs/release.wasm"
web.wasm32 = "native libs/threads.wasm"
; web.wasm32 = "native libs/unused.wasm"
[dependencies]
web = { "res://addons/probe/native libs/dependency.wasm": "" }
`);
        await writeFile(join(dir, 'scan.proj'), `<Project>
  <PropertyGroup>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <TwoDogGodotProjectFullPath>${xml(game)}</TwoDogGodotProjectFullPath>
    <TwoDogExportPack>false</TwoDogExportPack>
    <BaseIntermediateOutputPath>obj/</BaseIntermediateOutputPath>
  </PropertyGroup>
  <Import Project="${xml(targets)}" />
  <Target Name="Probe" DependsOnTargets="TwoDogWebExportSideModuleImports">
    <WriteLinesToFile File="exports.txt" Lines="@(EmccExportedFunction)" Overwrite="true" />
    <WriteLinesToFile File="inputs.txt" Lines="@(TwoDogWebExportInputs)" Overwrite="true" />
  </Target>
</Project>`);
        await run('dotnet', ['msbuild', 'scan.proj', '-t:Probe', '-v:minimal'], { cwd: dir, timeout: 45000 });
        const exports = (await readFile(join(dir, 'exports.txt'), 'utf8')).trim().split(/\r?\n/);
        assert(exports.includes('_hidden_cpp_function'));
        assert(exports.includes('_hidden_dependency'));
        assert(!exports.includes('_thread_only_function'));
        assert(!exports.includes('_unreferenced_function'));
        assert(!exports.includes('_ignored_host_function'));
        const inputs = (await readFile(join(dir, 'inputs.txt'), 'utf8')).trim().split(/\r?\n/).map(p => resolve(p));
        assert(inputs.includes(resolve(libs, 'release.wasm')), 'changes to ignored native libraries must invalidate the pack');
        assert(inputs.includes(resolve(libs, 'dependency.wasm')));
    } finally {
        assert(resolve(dir).startsWith(join(resolve(tmpdir()), 'twodog-side-modules-')));
        await rm(dir, { recursive: true, force: true });
    }
});
