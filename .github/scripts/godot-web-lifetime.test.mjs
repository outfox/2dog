import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import vm from 'node:vm';
import test from 'node:test';

// Load the real Emscripten libraries and run their postsets once, as a shared
// Blazor runtime does. Only browser resources and filesystem I/O are stubbed.
async function runtime() {
    const library = {};
    const context = vm.createContext({
        LibraryManager: { library },
        autoAddDeps() {},
        mergeInto: Object.assign,
        Module: {},
        setTimeout,
        clearInterval,
        URL: { revokeObjectURL() {} },
    });
    for (const name of ['os', 'input', 'display']) {
        const filename = `library_godot_${name}.js`;
        // CI downloads the native payload without checking out the Godot submodule.
        const url = process.env.GODOT_WEB_JS_LIB_DIR
            ? resolve(process.env.GODOT_WEB_JS_LIB_DIR, filename)
            : new URL(`../../godot/platform/web/js/libs/${filename}`, import.meta.url);
        // Emscripten emits the $ helpers as globals, rather than retaining the
        // library descriptor's lexical binding with the same name.
        const source = (await readFile(url, 'utf8')).replace(/^const (\w+) = /gm, 'var $1 = ');
        vm.runInContext(source, context);
    }
    for (const [name, value] of Object.entries(library)) {
        if (name.startsWith('$') && !name.includes('__')) context[name.slice(1)] = value;
    }
    for (const [name, value] of Object.entries(library)) {
        if (name.endsWith('__postset')) vm.runInContext(value, context);
    }
    context.GodotFS.sync = async () => {};
    context.GodotConfig.canvas = { style: {} };
    return context;
}

test('input listeners are removed after every engine shutdown', async () => {
    const { GodotOS, GodotEventListeners } = await runtime();
    const target = new EventTarget();
    let calls = 0;
    for (let lifetime = 1; lifetime <= 4; lifetime++) {
        GodotEventListeners.add(target, 'pointermove', () => calls++, false);
        target.dispatchEvent(new Event('pointermove'));
        assert.equal(calls, lifetime);
        await new Promise((resolve) => GodotOS.finish_async(resolve));
        target.dispatchEvent(new Event('pointermove'));
        assert.equal(calls, lifetime, `stale pointer callback after lifetime ${lifetime}`);
    }
});

test('module cleanup repeats while lifetime callbacks run only once', async () => {
    const r = await runtime();
    const cleared = [];
    const once = [];
    const hooks = ['GodotEventListeners', 'GodotIME', 'GodotDisplayVK', 'GodotDisplayCursor'];
    for (const name of hooks) {
        r[name].clear = () => cleared.push(name);
    }
    for (let lifetime = 1; lifetime <= 4; lifetime++) {
        // Audio and persistent file drops register anew for each lifetime.
        r.GodotOS.atexit((resolve) => { once.push(lifetime); resolve(); });
        await new Promise((resolve) => r.GodotOS.finish_async(resolve));
        assert.deepEqual(cleared, Array.from({ length: lifetime }, () => hooks).flat(),
            `missing cleanup in lifetime ${lifetime}`);
        assert.deepEqual(once, Array.from({ length: lifetime }, (_, i) => i + 1));
    }
});
