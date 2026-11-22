// Captures a screenshot every <stepMs> after the first frame and saves them as one labelled contact sheet.
import { setTimeout as delay } from 'node:timers/promises';
import { writeFileSync } from 'node:fs';
const [url, cdpPort, out, count = '20', stepMs = '2500'] = process.argv.slice(2);
const cdp = 'http://127.0.0.1:' + cdpPort;
async function open() {
  const page = await (await fetch(cdp + '/json/new?about:blank', { method: 'PUT' })).json();
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  let id = 0; const pending = new Map(), state = { frame: false };
  const call = (method, params = {}) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify({ id: n, method, params })); });
  ws.onmessage = e => { const m = JSON.parse(e.data);
    if (m.id) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(m.error) : p.resolve(m.result); }
    if (m.method === 'Runtime.consoleAPICalled' && /2dog: (first frame|ready)/.test(m.params.args?.[0]?.value ?? '')) state.frame = true; };
  return { call, state, close: async () => { ws.close(); await fetch(cdp + '/json/close/' + page.id); } };
}
const game = await open();
await game.call('Page.enable'); await game.call('Runtime.enable');
await game.call('Emulation.setDeviceMetricsOverride', { width: 480, height: 270, deviceScaleFactor: 1, mobile: false });
await game.call('Page.navigate', { url });
const deadline = Date.now() + 240000;
while (!game.state.frame && Date.now() < deadline) await delay(250);
const start = Date.now(), shots = [];
for (let i = 0; i < +count; i++) {
  await delay(Math.max(0, start + (i + 1) * +stepMs - Date.now()));
  shots.push({ t: ((Date.now() - start) / 1000).toFixed(1), data: (await game.call('Page.captureScreenshot', { format: 'jpeg', quality: 80 })).data });
}
await game.close();
const sheet = await open();
await sheet.call('Page.enable');
await sheet.call('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
const cells = shots.map(s => '<div style="position:relative"><img width=384 height=216 src="data:image/jpeg;base64,' + s.data + '"><b style="position:absolute;left:4px;top:2px;color:#0f0;font:14px monospace">' + s.t + 's</b></div>').join('');
await sheet.call('Page.setDocumentContent', { frameId: (await sheet.call('Page.getFrameTree')).frameTree.frame.id, html: '<body style="margin:0;background:#222;display:grid;grid-template-columns:repeat(5,384px)">' + cells + '</body>' });
await delay(1500);
writeFileSync(out, Buffer.from((await sheet.call('Page.captureScreenshot', { format: 'jpeg', quality: 85 })).data, 'base64'));
console.log('saved', out);
await sheet.close();
