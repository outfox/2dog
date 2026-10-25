import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test from 'node:test';
import vm from 'node:vm';

const run = promisify(execFile);
const targets = process.env.TWODOG_WEB_TARGETS
    ?? fileURLToPath(new URL('../../platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets', import.meta.url));
const nativeShell = process.env.GODOT_WEB_SHELL_FILE
    ?? fileURLToPath(new URL('../../godot/bin/web/template_release/shell/godot.js', import.meta.url));
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;');

test('pack-only exports carry renderer defaults into device initialization without changing HTML overrides', { timeout: 60000 }, async t => {
    const dir = await mkdtemp(join(tmpdir(), 'twodog-web-renderer-'));
    try {
        const shell = await readFile(nativeShell, 'utf8');
        const project = join(dir, 'project.godot');
        const output = join(dir, 'godot.js');
        const probe = join(dir, 'probe.proj');
        await writeFile(probe, `<Project>
  <Import Project="${xml(targets)}" />
  <Target Name="Patch">
    <TwoDogSetWebRendererDefault ProjectFile="${xml(project)}" ShellFile="${xml(output)}" WebGpuEnabled="$(HasWebGpu)" />
  </Target>
</Project>`);
        for (const [name, settings, enabled, driver, gpuAvailable = true] of [
            ['Mobile', '[rendering]\nrenderer/rendering_method.web="mobile"', true, 'webgpu'],
            ['Forward+', '[rendering]\nrenderer/rendering_method.web="forward_plus"', true, 'webgpu'],
            ['Forward+ on an unsupported runtime', '[rendering]\nrenderer/rendering_method.web="forward_plus"', false, 'opengl3'],
            ['Compatibility', '[rendering]\nrenderer/rendering_method.web="gl_compatibility"', true, 'opengl3'],
            ['default project', '', true, 'opengl3'],
            ['unsupported runtime', '[rendering]\nrenderer/rendering_method.web="mobile"', false, 'opengl3'],
            ['Mobile without a browser GPU', '[rendering] ; comment\nrenderer/rendering_method.web="mobile"', true, 'webgpu', false],
            ['other platform and commented settings', '[rendering]\n;renderer/rendering_method.web="mobile"\nrenderer/rendering_method.mobile="mobile"\n[application]\nrenderer/rendering_method.web="mobile"', true, 'opengl3'],
        ]) {
            await t.test(name, async () => {
                await writeFile(project, settings);
                await writeFile(output, shell);
                await run('dotnet', ['msbuild', probe, '-t:Patch', `-p:HasWebGpu=${enabled}`, '-v:quiet'], { timeout: 15000 });
                // Incremental publishes can receive an already patched shell.
                if (name === 'Mobile')
                    await run('dotnet', ['msbuild', probe, '-t:Patch', `-p:HasWebGpu=${enabled}`, '-v:quiet'], { timeout: 15000 });
                const calls = [];
                const device = { lost: new Promise(() => {}), addEventListener() {} };
                const context = vm.createContext({
                    console, Response, ReadableStream, window: {}, requestAnimationFrame() {},
                    fetch: async () => new Response(new Uint8Array()),
                    navigator: { gpu: { async requestAdapter() {
                        return { features: new Set(), limits: {}, async requestDevice() { return device; } };
                    } } },
                });
                // Stub only the binary/runtime and network; exercise the deployed Engine and config.
                vm.runInContext((await readFile(output, 'utf8')).replace('const Godot = async', 'let Godot = async'), context);
                context.capture = config => { calls.push(config); return { initFS: async () => {} }; };
                vm.runInContext('Godot = async config => capture(config);', context);
                const engine = new context.window.Engine({ canvas: {} });
                assert.equal(engine.config.renderingDriver, driver);
                if (!gpuAvailable) context.navigator.gpu = undefined;
                await engine.init('godot');
                assert.equal(calls.length, 1);
                assert.equal(engine.config.renderingDriver, gpuAvailable ? driver : 'opengl3');
                assert.equal(calls[0].preinitializedWebGPUDevice, enabled && driver === 'webgpu' && gpuAvailable ? device : undefined);
                assert.equal(new context.window.Engine({ renderingDriver: 'opengl3' }).config.renderingDriver, 'opengl3');
            });
        }
    } finally {
        assert(resolve(dir).startsWith(join(resolve(tmpdir()), 'twodog-web-renderer-')));
        await rm(dir, { recursive: true, force: true });
    }
});
