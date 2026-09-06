import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../../twodog.blazor/wwwroot/2dog.blazor.js', import.meta.url), 'utf8');
let generation = 0;
const deferred = () => {
    let resolve;
    const promise = new Promise((r) => { resolve = r; });
    return { promise, resolve };
};

async function view(initFS = async () => {}) {
    const calls = [];
    globalThis.document = { baseURI: 'https://example.test/' };
    globalThis.window = { devicePixelRatio: 1 };
    globalThis.ResizeObserver = class {
        observe() { calls.push('observe'); }
        disconnect() { calls.push('disconnect'); }
    };
    globalThis.Blazor = { runtime: { Module: {
        initFS,
        copyToFS: () => calls.push('copy'),
        initConfig: () => calls.push('configure'),
    } } };
    const module = await import(`data:text/javascript,${encodeURIComponent(source)}#${generation++}`);
    const canvas = { tabIndex: -1, clientWidth: 640, clientHeight: 480 };
    const options = { packUrl: 'godot.pck', packName: 'godot.pck', resize: 0, locale: 'en' };
    return { ...module, canvas, options, calls };
}

test('release during filesystem initialization prevents late fetch and canvas setup', async () => {
    const fs = deferred();
    const v = await view(() => fs.promise);
    globalThis.fetch = async () => { throw new Error('A cancelled view must not fetch'); };
    const starting = v.prepare(v.canvas, v.options);
    v.release(v.canvas);
    fs.resolve();
    await assert.rejects(starting, { name: 'AbortError' });
    assert.deepEqual(v.calls, []);
});

test('release while reading the pack prevents overwriting the next view runtime', async () => {
    const body = deferred();
    const reading = deferred();
    const v = await view();
    globalThis.fetch = async () => ({ ok: true, arrayBuffer() { reading.resolve(); return body.promise; } });
    const starting = v.prepare(v.canvas, v.options);
    await reading.promise;
    v.release(v.canvas);
    body.resolve(new ArrayBuffer(0));
    await assert.rejects(starting, { name: 'AbortError' });
    assert.deepEqual(v.calls, []);
});

test('successful lifetimes release their observers and configure a fresh canvas', async () => {
    const v = await view();
    globalThis.fetch = async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) });
    await v.prepare(v.canvas, v.options);
    v.release(v.canvas);
    v.release(v.canvas);
    const nextCanvas = { tabIndex: -1, clientWidth: 800, clientHeight: 600 };
    await v.prepare(nextCanvas, v.options);
    v.release(nextCanvas);
    assert.deepEqual(v.calls, ['copy', 'observe', 'configure', 'disconnect', 'copy', 'observe', 'configure', 'disconnect']);
    assert.equal(nextCanvas.width, 800);
    assert.equal(nextCanvas.height, 600);
});

test('a replacement view joins filesystem initialization left behind by a cancelled view', async () => {
    const fs = deferred();
    let mounts = 0;
    const v = await view(() => { mounts++; return fs.promise; });
    globalThis.fetch = async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(0) });
    const first = v.prepare(v.canvas, v.options);
    const cancelled = assert.rejects(first, { name: 'AbortError' });
    v.release(v.canvas);
    const nextCanvas = { tabIndex: -1, clientWidth: 800, clientHeight: 600 };
    const next = v.prepare(nextCanvas, v.options);
    fs.resolve();
    await Promise.all([cancelled, next]);
    assert.equal(mounts, 1);
    assert.deepEqual(v.calls, ['copy', 'observe', 'configure']);
    v.release(nextCanvas);
});
