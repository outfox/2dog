// Opens the page, waits for the first frame plus a settle delay, and saves a screenshot.
import { setTimeout as delay } from 'node:timers/promises';
import { writeFileSync } from 'node:fs';
const [url, cdpPort, out, settleMs = '25000'] = process.argv.slice(2);
const cdp = 'http://127.0.0.1:' + cdpPort;
const page = await (await fetch(cdp + '/json/new?about:blank', { method: 'PUT' })).json();
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0, frame = false; const pending = new Map();
const call = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify({ id: n, method, params })); });
ws.onmessage = e => { const m = JSON.parse(e.data);
  if (m.id) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(m.error) : p.resolve(m.result); }
  if (m.method === 'Runtime.consoleAPICalled' && /2dog: (first frame|ready)/.test(m.params.args?.[0]?.value ?? '')) frame = true; };
await call('Page.enable'); await call('Runtime.enable');
await call('Emulation.setDeviceMetricsOverride', { width: 960, height: 540, deviceScaleFactor: 1, mobile: false });
await call('Page.navigate', { url });
const deadline = Date.now() + 240000;
while (!frame && Date.now() < deadline) await delay(250);
await delay(+settleMs);
const { data } = await call('Page.captureScreenshot', { format: 'png' });
writeFileSync(out, Buffer.from(data, 'base64'));
console.log('saved', out, frame ? '' : '(no first frame)');
ws.close(); await fetch(cdp + '/json/close/' + page.id);
