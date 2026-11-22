// CPU-profiles the page and every worker until the first frame; prints self time per thread and top functions.
import { setTimeout as delay } from 'node:timers/promises';
import { writeFileSync } from 'node:fs';
const [url, cdpPort, out] = process.argv.slice(2);
const cdp = 'http://127.0.0.1:' + cdpPort;
const v = await (await fetch(cdp + '/json/version')).json();
const ws = new WebSocket(v.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0; const pending = new Map(); const sessions = new Map(); let frame = false;
const call = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
  const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify({ id: n, method, params, sessionId }));
});
const attach = async (sessionId, info) => {
  if (sessions.has(sessionId)) return; sessions.set(sessionId, info);
  await call('Profiler.enable', {}, sessionId);
  await call('Profiler.setSamplingInterval', { interval: 1000 }, sessionId);
  await call('Profiler.start', {}, sessionId);
  await call('Runtime.enable', {}, sessionId).catch(() => {});
  await call('Target.setAutoAttach', { autoAttach: true, waitForDebuggerOnStart: true, flatten: true }, sessionId).catch(() => {});
  await call('Runtime.runIfWaitingForDebugger', {}, sessionId).catch(() => {});
};
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.id) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(m.error) : p.resolve(m.result); return; }
  if (m.method === 'Target.attachedToTarget') attach(m.params.sessionId, m.params.targetInfo);
  if (m.method === 'Runtime.consoleAPICalled' && /2dog: (first frame|ready)/.test(m.params.args?.[0]?.value ?? '')) frame = true;
};
const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
await call('Page.enable', {}, sessionId);
await attach(sessionId, { type: "page", url }).catch(() => {});
await call('Page.navigate', { url }, sessionId);
const deadline = Date.now() + 120000;
while (!frame && Date.now() < deadline) await delay(200);
const report = [];
for (const [sid, info] of sessions) {
  const r = await call('Profiler.stop', {}, sid).catch(() => null);
  if (!r) continue;
  const p = r.profile, byId = new Map(p.nodes.map(n => [n.id, n])), self = new Map();
  const dt = p.timeDeltas; let total = 0;
  p.samples.forEach((s, i) => { const n = byId.get(s); const f = n.callFrame; const k = (f.functionName || '(anon)') + ' ' + (f.url.split('/').pop()) + ':' + f.lineNumber; self.set(k, (self.get(k) || 0) + (dt[i] || 0)); total += dt[i] || 0; });
  const top = [...self].sort((a, b) => b[1] - a[1]).slice(0, 12).map(([k, us]) => `   ${(us / 1000).toFixed(0).padStart(7)} ms  ${k}`);
  report.push(`${info.type} ${info.url.split('/').pop()} ${sid.slice(0, 6)} total ${(total / 1000).toFixed(0)} ms`, ...top);
}
console.log(report.join('\n'));
if (out) writeFileSync(out, report.join('\n'));
await call('Target.closeTarget', { targetId });
ws.close();
