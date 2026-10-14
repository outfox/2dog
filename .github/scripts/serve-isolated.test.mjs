import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const script = fileURLToPath(new URL('serve-isolated.mjs', import.meta.url));

test('serves bundles cross-origin isolated and stays inside the root', { timeout: 30000 }, async () => {
    const dir = await mkdtemp(join(tmpdir(), 'twodog-serve-isolated-'));
    const server = spawn(process.execPath, [script, join(dir, 'bundle'), '0']);
    try {
        await mkdir(join(dir, 'bundle', '_framework'), { recursive: true });
        await writeFile(join(dir, 'bundle', 'index.html'), '<html></html>');
        await writeFile(join(dir, 'bundle', '_framework', 'dotnet.native.wasm'), Buffer.from([0, 97, 115, 109]));
        await writeFile(join(dir, 'secret.txt'), 'outside');
        const [line] = await once(server.stdout, 'data');
        const origin = /on (http:\/\/\S+)/.exec(String(line))[1];

        const index = await fetch(`${origin}/`);
        assert.equal(index.status, 200);
        assert.equal(index.headers.get('cross-origin-opener-policy'), 'same-origin');
        assert.equal(index.headers.get('cross-origin-embedder-policy'), 'require-corp');
        assert.equal(await index.text(), '<html></html>');

        const wasm = await fetch(`${origin}/_framework/dotnet.native.wasm`);
        assert.equal(wasm.headers.get('content-type'), 'application/wasm');
        assert.equal((await wasm.arrayBuffer()).byteLength, 4);

        for (const path of ['/missing.js', '/%2e%2e/secret.txt', '/..%2fsecret.txt']) {
            const response = await fetch(`${origin}${path}`);
            assert.equal(response.status, 404, path);
            assert.equal(response.headers.get('cross-origin-embedder-policy'), 'require-corp');
        }
    } finally {
        server.kill();
        await rm(dir, { recursive: true, force: true });
    }
});
