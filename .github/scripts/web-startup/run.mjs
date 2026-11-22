// Opens the page through CDP and prints per-frame WebGPU call counts for the first frames.
import { setTimeout as delay } from 'node:timers/promises';
const [url, frames = '8', cdpPort = '18471'] = process.argv.slice(2);
const cdp = 'http://127.0.0.1:' + cdpPort;
const page = await (await fetch(cdp+'/json/new?about:blank',{method:'PUT'})).json();
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j});
const pending = new Map(); let id = 0; const samples = []; let firstFrame = null, proxyAtFrame = null; const errors = [];
const call = (method,params={}) => new Promise((resolve,reject)=>{const n=++id;pending.set(n,{resolve,reject});ws.send(JSON.stringify({id:n,method,params}));});
const t0=Date.now(); const log=[]; ws.onmessage = e => { const m = JSON.parse(e.data);
  if (m.method === 'Runtime.consoleAPICalled') log.push(((Date.now()-t0)/1000).toFixed(2)+' '+m.params.args.map(x=>x.value??x.description).join(' '));
  if (m.id) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(m.error) : p.resolve(m.result); }
  if (m.method === 'Runtime.consoleAPICalled') { const line = m.params.args.map(x=>x.value??x.description).join(' ');
    if (line.startsWith('PROXY ')) { const d=JSON.parse(line.slice(6)); (globalThis.proxy ??= {})[d.thread]=d.stats; } else if (line.startsWith('FRAME ')) samples.push(JSON.parse(line.slice(6)));
    else if (/2dog: (first frame|ready)/.test(line)) { firstFrame = line; proxyAtFrame = structuredClone(globalThis.proxy ?? {}); }
    else if (/ERROR|Shader cache miss|Uncaught/.test(line)) errors.push(line); } };
try {
  await call('Page.enable'); await call('Runtime.enable');
  await call('Emulation.setDeviceMetricsOverride',{width:960,height:540,deviceScaleFactor:1,mobile:false});
  await call('Page.addScriptToEvaluateOnNewDocument',{source:`new BroadcastChannel('proxyStats').onmessage=e=>console.log('PROXY '+JSON.stringify(e.data));new BroadcastChannel('startupFrames').onmessage=e=>console.log('FRAME '+JSON.stringify(e.data));`});
  await call('Page.navigate',{url});
  const deadline = Date.now()+300000;
  while (Date.now() < deadline && samples.length < +frames) await delay(500);
  await delay(+(process.env.LATE ?? 1) * 1000); // LATE=<seconds> keeps listening for late slow frames.
  console.log(firstFrame);
  for (const s of samples) {
    if (s.span) { console.log(`frames to ${s.frame}: ${(300000 / s.span).toFixed(1)} fps, ${s.passes.toFixed(1)} render passes and ${s.draws.toFixed(0)} draws per frame`); continue; }
    const c = s.calls, f = k => c[k] ? `${c[k][0]} (${Math.round(c[k][1])} ms)` : '0';
    console.log(`frame ${s.frame}: wall ${Math.round(s.wallMs)} ms | renderPipelines ${f('createRenderPipeline')} | computePipelines ${f('createComputePipeline')} | async render ${f('createRenderPipelineAsync')} compute ${f('createComputePipelineAsync')} | shaderModules ${f('createShaderModule')} | submits ${f('submit')}`);
    if (process.env.LABELS && s.labels) for (const [k,v] of Object.entries(s.labels).sort()) console.log('     ', v, k);
  }
  const names = Object.fromEntries([...(await (await fetch(url+'_framework/dotnet.native.js')).text()).matchAll(/WebGPU.ownerCalls\[(\d+)\] = deviceThread_(\w+)/g)].map(m=>[m[1],m[2]]));
  for (const [t,st] of Object.entries(proxyAtFrame ?? {})) for (const [k,[n,ms,max]] of Object.entries(st).sort((a,b)=>b[1][1]-a[1][1]).slice(0,8)) console.log('proxy', t, names[k] ?? k, n, 'calls', Math.round(ms), 'ms total, max', Math.round(max), 'ms');
  if (errors.length) console.log('errors/misses:', errors.length, errors.slice(0,5));
} finally { if (process.env.LOG) (await import('node:fs')).writeFileSync(process.env.LOG, log.join(String.fromCharCode(10))); ws.close(); await fetch(cdp+'/json/close/'+page.id); }
