// Drive the isolated browser-wasm probe and inspect actual console severity.
// Usage: web-exception-diagnostics-smoke.mjs <probe-url> [debug-port]
import assert from 'node:assert/strict';

const [url, debugPort = '9222'] = process.argv.slice(2);
assert(url, 'A published browser-exceptions probe URL is required');
const timeout = setTimeout(() => {
    console.error('Browser exception diagnostics smoke timed out');
    process.exit(1);
}, 60000);
timeout.unref();
const pages = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
const page = pages.find((p) => p.type === 'page' && p.url.startsWith(url));
assert(page?.webSocketDebuggerUrl, `Chrome does not expose ${url}`);
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
    ws.addEventListener('open', resolve);
    ws.addEventListener('error', reject);
});
let id = 0;
let logs = [];
const pending = new Map();
ws.addEventListener('message', (event) => {
    const data = JSON.parse(event.data);
    if (data.id) {
        pending.get(data.id)?.(data);
        pending.delete(data.id);
    } else if (data.method === 'Runtime.consoleAPICalled') {
        logs.push({ type: data.params.type,
            text: data.params.args.map((a) => a.value ?? a.description ?? '').join(' ') });
    }
});
const send = (method, params = {}) => new Promise((resolve) => {
    const next = ++id;
    pending.set(next, resolve);
    ws.send(JSON.stringify({ id: next, method, params }));
});
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
try {
    await send('Runtime.enable');
    await send('Page.enable');
    for (const mode of ['caught', 'first-chance', 'unobserved', 'broken-stderr', 'unhandled-first-chance', 'unhandled']) {
        logs = [];
        const probeUrl = new URL(url);
        probeUrl.searchParams.set('mode', mode);
        await send('Page.navigate', { url: probeUrl.href });
        const terminal = mode.startsWith('unhandled') ? /Unhandled Exception|runtime terminating/ : /PROBE_COMPLETE/;
        const deadline = Date.now() + 10000;
        while (!logs.some((l) => terminal.test(l.text)) && Date.now() < deadline) await delay(100);
        assert(logs.some((l) => terminal.test(l.text)), `${mode} did not finish: ${JSON.stringify(logs)}`);
        await delay(100);
        const errors = logs.filter((l) => l.type === 'error').map((l) => l.text).join('\n');
        if (mode === 'first-chance') {
            assert.match(errors, /2dog: First-chance managed exception[\s\S]*retained-task/);
            assert.match(errors, /FailAsync/);
            assert.doesNotMatch(errors, /caught-after-disable/);
        } else if (mode === 'unobserved') {
            assert.equal(errors.match(/2dog: Unobserved task exception/g)?.length, 1);
            assert.match(errors, /abandoned-task/);
            assert.match(errors, /FailAsync/);
        } else if (mode.startsWith('unhandled')) {
            // Mono's ThreadPool fail-fast path bypasses AppDomain.UnhandledException;
            // its native stderr still reaches console.error. First-chance mode
            // must additionally report the original throw before termination.
            assert.match(errors, /async-void-failure/);
            assert.match(errors, /ThrowAsyncVoid/);
            if (mode === 'unhandled-first-chance')
                assert.match(errors, /2dog: First-chance managed exception/);
        } else {
            assert.equal(errors, '');
        }
        if (mode === 'first-chance' || mode === 'unobserved' || mode.startsWith('unhandled'))
            assert.match(errors, /inner-failure/);
        console.log(`Browser exception diagnostics: ${mode} passed`);
    }
} finally {
    ws.close();
    clearTimeout(timeout);
}
