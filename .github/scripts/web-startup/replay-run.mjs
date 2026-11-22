// Opens the replay page through CDP and prints its REPLAY result line.
import { setTimeout as delay } from 'node:timers/promises';
const [url, cdpPort] = process.argv.slice(2);
const cdp = 'http://127.0.0.1:' + cdpPort;
const page = await (await fetch(cdp + '/json/new?about:blank', { method: 'PUT' })).json();
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let id = 0, result = null;
const send = (method, params = {}) => ws.send(JSON.stringify({ id: ++id, method, params }));
ws.onmessage = e => { const m = JSON.parse(e.data);
  const line = m.method === 'Runtime.consoleAPICalled' ? m.params.args.map(x => x.value ?? x.description).join(' ') : m.method === 'Runtime.exceptionThrown' ? 'EXCEPTION ' + JSON.stringify(m.params.exceptionDetails.exception?.description ?? m.params.exceptionDetails.text) : null;
  if (line?.startsWith('REPLAY ') || line?.startsWith('EXCEPTION')) result = line; else if (line) console.log('  console:', line.slice(0, 300)); };
send('Runtime.enable'); send('Page.enable'); send('Page.navigate', { url });
const deadline = Date.now() + 600000;
while (!result && Date.now() < deadline) await delay(250);
console.log(result ?? 'timeout');
ws.close(); await fetch(cdp + '/json/close/' + page.id);
