import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import test from 'node:test';

test('browser smoke waits for the requested mode when the initial page finishes during navigation', async () => {
    const url = 'http://localhost/index.html';
    const source = readFileSync(new URL('./web-exception-diagnostics-smoke.mjs', import.meta.url), 'utf8')
        .replace(/^import .*;\r?\n/gm, '')
        .replace(/for \(const mode of \[[\s\S]*?\]\)/, "for (const mode of ['forgotten'])");
    const output = [];
    class Socket extends EventTarget {
        constructor() {
            super();
            queueMicrotask(() => this.dispatchEvent(new Event('open')));
        }
        message(data) {
            this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(data) }));
        }
        log(type, text) {
            this.message({ method: 'Runtime.consoleAPICalled', params: { type, args: [{ value: text }] } });
        }
        send(json) {
            const { id, method } = JSON.parse(json);
            queueMicrotask(() => {
                if (method === 'Page.navigate') {
                    this.log('log', 'PROBE_COMPLETE:caught');
                    setTimeout(() => {
                        this.log('error', '2dog: Fire-and-forget task exception\nforgotten-task inner-failure FailAsync');
                        this.log('log', 'PROBE_COMPLETE:forgotten');
                    }, 250);
                }
                this.message({ id, result: {} });
            });
        }
        close() { }
    }
    await runInNewContext(`(async () => { ${source}\n })()`, {
        assert, process: { argv: ['node', 'smoke', url] }, WebSocket: Socket, URL, setTimeout, clearTimeout,
        fetch: async () => ({ json: async () => [{ type: 'page', url, webSocketDebuggerUrl: 'ws://probe' }] }),
        console: { log: (line) => output.push(line), error: (line) => output.push(line) },
    });
    assert.deepEqual(output, ['Browser exception diagnostics: forgotten passed']);
});
