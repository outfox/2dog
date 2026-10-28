import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test from 'node:test';

const run = promisify(execFile);
const targets = process.env.TWODOG_WEB_TARGETS
    ?? fileURLToPath(new URL('../../platforms/twodog.browser-wasm/build/2dog.browser-wasm.targets', import.meta.url));
const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;');

const presets = baker => `[preset.0]

name="Windows Desktop"
platform="Windows Desktop"

[preset.0.options]

shader_baker/enabled=true

[preset.1]

name="Web"
platform="Web"

[preset.1.options]

shader_baker/enabled=${baker}
`;

test('web exports bake WebGPU shaders only when the Web preset enables the baker for a WebGPU method', { timeout: 60000 }, async t => {
    const dir = await mkdtemp(join(tmpdir(), 'twodog-web-bake-'));
    try {
        const project = join(dir, 'project.godot');
        const presetsFile = join(dir, 'export_presets.cfg');
        const probe = join(dir, 'probe.proj');
        await writeFile(probe, `<Project>
  <Import Project="${xml(targets)}" />
  <Target Name="Probe">
    <TwoDogReadWebShaderBake ProjectFile="${xml(project)}" PresetsFile="${xml(presetsFile)}" PresetName="Web">
      <Output TaskParameter="RenderingMethod" PropertyName="Method"/>
    </TwoDogReadWebShaderBake>
    <Message Importance="high" Text="BAKE_METHOD=[$(Method)]"/>
  </Target>
</Project>`);
        for (const [name, baker, settings, expected] of [
            ['Forward+', 'true', '[rendering]\nrenderer/rendering_method.web="forward_plus"', 'forward_plus'],
            ['Mobile', 'true', '[rendering]\nrenderer/rendering_method.web="mobile"', 'mobile'],
            ['baker disabled', 'false', '[rendering]\nrenderer/rendering_method.web="forward_plus"', ''],
            ['Compatibility', 'true', '[rendering]\nrenderer/rendering_method.web="gl_compatibility"', ''],
            ['default web method', 'true', '[application]\nconfig/name="x"', ''],
        ]) {
            await t.test(name, async () => {
                await writeFile(project, settings);
                await writeFile(presetsFile, presets(baker));
                const { stdout } = await run('dotnet', ['msbuild', probe, '-t:Probe', '-v:minimal', '-nologo'], { timeout: 30000 });
                assert.match(stdout, new RegExp(`BAKE_METHOD=\\[${expected}\\]`));
            });
        }
    } finally {
        assert(resolve(dir).startsWith(join(resolve(tmpdir()), 'twodog-web-bake-')));
        await rm(dir, { recursive: true, force: true });
    }
});
