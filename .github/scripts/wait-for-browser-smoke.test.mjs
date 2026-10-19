import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('./wait-for-browser-smoke.mjs', import.meta.url), 'utf8')
    .replace(/^#![^\n]*\n/, '');
const targetUrl = 'http://127.0.0.1:8000/';

async function probe(mode) {
    const output = [];
    const evaluations = [];
    const process = { argv: ['node', 'probe', targetUrl, '1000', '9222'], env: {} };
    let contextId;
    let uniqueContextId;
    let replaced = false;
    class Socket extends EventTarget {
        constructor() {
            super();
            queueMicrotask(() => this.dispatchEvent(new Event('open')));
        }
        message(data) {
            this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(data) }));
        }
        context(id, frameId = 'main', isDefault = true, origin = new URL(targetUrl).origin,
            uniqueId = `context-${id}`) {
            this.message({ method: 'Runtime.executionContextCreated', params: {
                context: { id, uniqueId, origin, auxData: { frameId, isDefault } },
            } });
        }
        send(json) {
            const { id, method, params } = JSON.parse(json);
            queueMicrotask(() => {
                if (method === 'Page.getFrameTree') {
                    this.message({ id, result: { frameTree: { frame: { id: 'main' } } } });
                } else if (method === 'Runtime.enable') {
                    this.context(2, 'subframe');
                    this.context(3, 'main', false);
                    this.context(4, 'main', true, '');
                    this.message({ id, result: {} });
                    setTimeout(() => {
                        contextId = 1;
                        uniqueContextId = 'context-1';
                        this.context(contextId);
                    }, 20);
                } else if (method === 'Runtime.evaluate') {
                    evaluations.push(params.uniqueContextId);
                    if (contextId === undefined || params.uniqueContextId !== uniqueContextId) {
                        this.message({ id, error: { message: 'Cannot find default execution context' } });
                    } else if (mode === 'navigation' && !replaced) {
                        replaced = true;
                        this.message({ method: 'Runtime.executionContextDestroyed',
                            params: { executionContextId: contextId } });
                        uniqueContextId = 'replacement';
                        this.context(contextId, 'main', true, new URL(targetUrl).origin, uniqueContextId);
                        this.message({ id, error: { message: 'Execution context was destroyed' } });
                    } else if (mode === 'protocol-error') {
                        this.message({ id, error: { message: 'Unrelated protocol failure' } });
                    } else {
                        this.message({ id, result: { result: { value: {
                            smoke: mode === 'app-failure' ? 'failed' : 'passed',
                            required: null, readyState: 'complete', failureNotice: '',
                        } } } });
                    }
                } else {
                    this.message({ id, result: {} });
                }
            });
        }
        close() {
            this.dispatchEvent(new Event('close'));
        }
    }
    await runInNewContext(`(async () => { ${source}\n })()`, {
        process, WebSocket: Socket, URL, setTimeout, clearTimeout,
        fetch: async () => ({ ok: true, json: async () => [
            { type: 'page', url: targetUrl, webSocketDebuggerUrl: 'ws://probe' },
        ] }),
        console: { log: (line) => output.push(line), error: (line) => output.push(line) },
    });
    return { exitCode: process.exitCode ?? 0, output: output.join('\n'), evaluations };
}

test('waits for the main frame default context before inspecting the smoke marker', async () => {
    const result = await probe('delayed');
    assert.equal(result.exitCode, 0, result.output);
    assert.deepEqual(result.evaluations, ['context-1']);
    assert.match(result.output, /data-twodog-smoke=passed/);
});

test('waits for the replacement context when navigation invalidates an evaluation', async () => {
    const result = await probe('navigation');
    assert.equal(result.exitCode, 0, result.output);
    assert.deepEqual(result.evaluations, ['context-1', 'replacement']);
});

test('still fails for unrelated protocol errors', async () => {
    const result = await probe('protocol-error');
    assert.equal(result.exitCode, 1);
    assert.match(result.output, /Unrelated protocol failure/);
});

test('still fails when the app reports a failed smoke marker', async () => {
    const result = await probe('app-failure');
    assert.equal(result.exitCode, 1);
    assert.match(result.output, /data-twodog-smoke=failed/);
});
