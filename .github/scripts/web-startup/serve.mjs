// Serves a threaded 2dog AppBundle with COOP/COEP and per-frame WebGPU call counts for the first frames.
// Usage: serve.mjs <dir> <port> [frames]. With CAPTURE=<file>, every GPUDevice create call is also written there as
// JSON at frame <frames - 1>, for replay/index.html. ARGS="--a --b" adds engine arguments. Later frames are reported
// too when they create shaders or pipelines or take over 250 ms. DRAWS=1 also reports render passes, draw calls and
// frame rate, averaged over every 300 frames. A request to /__quit stops the server.
import { createServer } from 'node:http';
import { createReadStream, readFileSync, statSync, writeFileSync } from 'node:fs';
import { resolve, sep, extname } from 'node:path';
const [dir, port, frames = '8'] = process.argv.slice(2);
const root = resolve(dir);
const js = '/_framework/dotnet.native.js';
const headers = {'Cross-Origin-Opener-Policy':'same-origin','Cross-Origin-Embedder-Policy':'require-corp','Cache-Control':'no-store'};
const types = {'.html':'text/html','.mjs':'text/javascript','.js':'text/javascript','.wasm':'application/wasm','.json':'application/json','.svg':'image/svg+xml'};
function instrument(source) {
  const proxyAnchor = 'return _emwgpuProxyToDevice(id, args.length, values);';
  if (!source.includes(proxyAnchor)) throw Error('Missing proxy anchor');
  source = source.replace(proxyAnchor, 'const t0 = performance.now(); try { return _emwgpuProxyToDevice(id, args.length, values); } finally { const g = (globalThis.__proxy ??= { st: {}, last: 0, ch: new BroadcastChannel("proxyStats"), tid: Math.random().toString(36).slice(2, 7) }); const e = (g.st[id] ??= [0, 0, 0]); const now = performance.now(), d = now - t0; e[0]++; e[1] += d; e[2] = Math.max(e[2], d); if (now - g.last > 250) { g.last = now; g.ch.postMessage({ thread: g.tid, stats: g.st }); } }');
  const anchor = 'Module["godotWebGPUCanvas"] = canvas;';
  if (!source.includes(anchor)) throw Error('Missing transferred game canvas');
  return source.replace(anchor, anchor + `
  {
    const report = new BroadcastChannel('startupFrames');
    let frame = 0, bucket = {}, labels = {}, since = performance.now();
    const capture = ${!!process.env.CAPTURE}, cap = [], ids = new WeakMap(); let nextId = 0;
    for (const [klass, names] of [[GPUDevice,['createShaderModule','createRenderPipeline','createComputePipeline','createRenderPipelineAsync','createComputePipelineAsync'].concat(capture ? ['createBindGroupLayout','createPipelineLayout'] : [])], [GPUQueue,['submit']]]) {
      for (const name of names) {
        const original = klass.prototype[name];
        klass.prototype[name] = function(...args) {
          const start = performance.now();
          if (name === 'createRenderPipeline' || name === 'createComputePipeline') { const d = args[0], k = (name[6] === 'C' ? 'compute:' : '') + (d.label||'?').replace(/^pipe#\\d+:/,'') + ' c' + Object.keys(d.fragment?.constants ?? d.vertex?.constants ?? d.compute?.constants ?? {}).length; labels[k] = (labels[k]||0)+1; }
          try { const r = original.apply(this,args); if (capture && klass === GPUDevice && !name.endsWith('Async')) { ids.set(r, ++nextId); cap.push({ op: name, id: nextId, desc: JSON.stringify(args[0], (k, v) => v && typeof v === 'object' && ids.has(v) ? { $ref: ids.get(v) } : v) }); } return r; }
          finally { const item = bucket[name] ??= [0,0]; item[0]++; item[1] += performance.now()-start; }
        };
      }
    }
    const drawStats = ${!!process.env.DRAWS}; let passes = 0, draws = 0, mark = performance.now();
    if (drawStats) {
      const begin = GPUCommandEncoder.prototype.beginRenderPass; GPUCommandEncoder.prototype.beginRenderPass = function(...a) { passes++; return begin.apply(this, a); };
      for (const name of ['draw', 'drawIndexed', 'drawIndirect', 'drawIndexedIndirect']) { const original = GPURenderPassEncoder.prototype[name]; GPURenderPassEncoder.prototype[name] = function(...a) { draws++; return original.apply(this, a); }; }
    }
    const get = GPUCanvasContext.prototype.getCurrentTexture;
    GPUCanvasContext.prototype.getCurrentTexture = function() {
      const t = get.call(this);
      if (frame < ${+frames}) { report.postMessage({ frame: ++frame, wallMs: performance.now()-since, calls: bucket, labels }); since = performance.now(); bucket = {}; labels = {}; if (capture && frame === ${+frames - 1}) fetch('/__capture', { method: 'POST', body: JSON.stringify(cap) }); }
      else { const now = performance.now(); frame++; if (now - since > 250 || Object.keys(bucket).some(k => k !== 'submit')) report.postMessage({ frame, wallMs: now - since, calls: bucket, labels }); since = now; bucket = {}; labels = {};
        if (drawStats && frame % 300 === 0) { report.postMessage({ frame, span: now - mark, passes: passes / 300, draws: draws / 300 }); mark = now; passes = draws = 0; } }
      return t;
    };
  }
  `);
}
createServer((req,res)=>{
  const name = new URL(req.url,'http://localhost').pathname;
  const path = resolve(root,'.'+(name==='/'?'/index.html':name));
  if (!path.startsWith(root+sep)) return res.writeHead(403).end();
  if (name === '/__quit') { res.writeHead(200, headers).end(); process.exit(0); }
  if (name === '/__capture' && req.method === 'POST') { const chunks = []; req.on('data', c => chunks.push(c)).on('end', () => { writeFileSync(process.env.CAPTURE, Buffer.concat(chunks)); res.writeHead(200, headers).end(); console.log('capture written', process.env.CAPTURE); }); return; }
  let stat; try { stat = statSync(path); } catch { return res.writeHead(404,headers).end(); }
  const common = {...headers,'content-type':types[extname(path)]??'application/octet-stream'};
  if (process.env.ARGS && path.endsWith('index.html')) { const body = readFileSync(path, 'utf8').replace("'args': [", "'args': [" + process.env.ARGS.split(' ').map(a => JSON.stringify(a) + ',').join(' ')); res.writeHead(200, common).end(body); return; }
  if (name === js) { const body = Buffer.from(instrument(readFileSync(path,'utf8'))); res.writeHead(200,{...common,'content-length':body.length}).end(body); return; }
  res.writeHead(200,{...common,'content-length':stat.size}); createReadStream(path).pipe(res);
}).listen(+port,'127.0.0.1',()=>console.log('serving',root,port));
