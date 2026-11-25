import { setTimeout as delay } from 'node:timers/promises';
import { mkdir, writeFile } from 'node:fs/promises';

const [url, port = '9233', timeout = '240000', output] = process.argv.slice(2);
if (!url) throw new Error('Usage: webgpu-mobile-smoke.mjs <url> [CDP port] [timeout ms] [screenshot directory]');
// The page's ?method= selects the renderer; ?expect=mobile pins a Forward+ downgrade on a limited adapter.
const query = new URL(url).searchParams;
const requested = query.get('method') ?? 'mobile';
const expected = query.get('expect') ?? requested;
const forwardPlus = expected === 'forward_plus';
const phases = forwardPlus ? 7 : 13;
const cdp = `http://127.0.0.1:${port}`;
const target = await (await fetch(`${cdp}/json/new?about:blank`, { method: 'PUT' })).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
const pending = new Map();
const errors = [];
let nextId = 0;
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const request = pending.get(message.id);
        pending.delete(message.id);
        if (message.error) request?.reject(new Error(JSON.stringify(message.error)));
        else request?.resolve(message.result);
    } else if (message.method === 'Runtime.consoleAPICalled') {
        const text = message.params.args.map(arg => arg.value ?? arg.description ?? arg.type).join(' ');
        console.log(`${message.params.type}: ${text}`);
        // The expected downgrade warning and its source location arrive as two console messages.
        const downgrade = requested !== expected &&
            (text.includes('Defaulting to Mobile renderer') || /^\s*at: RendererCompositorRD \(/.test(text));
        if ((message.params.type === 'error' || message.params.type === 'warning') && !downgrade) errors.push(text);
    } else if (message.method === 'Runtime.exceptionThrown') {
        errors.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text);
    }
};
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const id = ++nextId;
        pending.set(id, { resolve, reject });
        socket.send(JSON.stringify({ id, method, params }));
        setTimeout(() => {
            if (pending.delete(id)) reject(new Error(`CDP timeout: ${method}`));
        }, Number(timeout)).unref();
    });
}
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function waitFor(expression) {
    const deadline = Date.now() + Number(timeout);
    while (Date.now() < deadline) {
        if (errors.length) throw new Error(errors.join('\n'));
        if (await evaluate(expression)) return;
        await delay(250);
    }
    throw new Error(`Timed out: ${expression}`);
}
try {
    await send('Runtime.enable');
    await send('Page.enable');
    if (query.get('threads') === '1') {
        await send('Emulation.setDeviceMetricsOverride', { width: 512, height: 512, deviceScaleFactor: 1, mobile: false });
    }
    // A new target can open behind other tabs (Edge first-run pages), where requestAnimationFrame never runs.
    await send('Page.bringToFront');
    await send('Page.navigate', { url });
    if (output) await mkdir(output, { recursive: true });
    const samples = [];
    for (let phase = 0; phase < phases; phase++) {
        if (phase) await evaluate(`document.documentElement.dataset.requestPhase='${phase}'`);
        await waitFor(`document.documentElement?.dataset.readyPhase==='${phase}'`);
        // Like the templates' splash, wait out boot until shaders are prewarmed and frames complete.
        if (phase === 0) await waitFor('!Engine.reportsFrames || Number(document.documentElement.dataset.frames) > 4');
        if (phase === 0) {
            const actual = await evaluate('document.documentElement.dataset.renderingMethod');
            if (actual !== expected) throw new Error(`WebGPU ${requested} ran ${actual}, expected ${expected}.`);
            if (query.has('threads') && await evaluate('document.documentElement.dataset.threads') !== query.get('threads'))
                throw new Error('The browser runtime does not match the requested thread mode.');
            if (query.get('threads') === '1') {
                await send('Emulation.setDeviceMetricsOverride', { width: 640, height: 480, deviceScaleFactor: 1, mobile: false });
                await waitFor("document.querySelector('canvas').width === 640 && document.querySelector('canvas').height === 480");
            }
            console.log('BROWSER_RENDERING_METHOD', actual);
        }
        // Pipelines new after boot (lightmaps, point/depth materials) compile in the background with their draws
        // skipped until ready, and no event reports that: poll until the target appears.
        const settle = Date.now() + 30000;
        let pixel, verdict;
        do {
            await delay(200);
            pixel = await capture(phase);
            verdict = check(phase, pixel);
        } while (verdict?.error && Date.now() < settle);
        console.log(forwardPlus ? 'BROWSER_FORWARD_PLUS_PIXEL' : 'BROWSER_MOBILE_PIXEL', phase, pixel);
        if (verdict?.error) throw new Error(verdict.error);
        if (verdict) console.log(verdict.passed);
        else samples.push((pixel[0] + pixel[1] + pixel[2]) / (3 * 255));
    }
    await evaluate(`document.documentElement.dataset.samples='${samples.join(',')}';document.documentElement.dataset.requestPhase='${phases}'`);
    await waitFor(`document.documentElement?.dataset.twodogSmoke==='passed'`);
    if (query.get('threads') === '1') {
        await waitFor('window.workerIndexedDraw === true');
    }
    if (errors.length) throw new Error(errors.join('\n'));
    console.log(forwardPlus ? 'BROWSER_FORWARD_PLUS_CLUSTERED_LIGHTING_PASSED' : 'BROWSER_MOBILE_LIGHT_LIST_PASSED', samples);
} finally {
    socket.close();
    await fetch(`${cdp}/json/close/${target.id}`);
}

async function capture(phase) {
    const clip = await evaluate(`(() => {
        const r = document.querySelector('canvas').getBoundingClientRect();
        return { x: r.x, y: r.y, width: r.width, height: r.height, scale: 1 };
    })()`);
    const shot = await send('Page.captureScreenshot', { format: 'png', clip });
    if (output) await writeFile(`${output}/phase-${phase}.png`, Buffer.from(shot.data, 'base64'));
    return await evaluate(`(async () => {
        const blob = await (await fetch('data:image/png;base64,${shot.data}')).blob();
        const bitmap = await createImageBitmap(blob);
        const canvas = document.createElement('canvas');
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
        const context = canvas.getContext('2d');
        context.drawImage(bitmap, 0, 0);
        return [...context.getImageData(${forwardPlus && phase === 4 ? '8, 8' : 'canvas.width / 2, canvas.height / 2'}, 1, 1).data];
    })()`);
}

// Returns undefined for light-list samples, else { passed } or { error }.
function check(phase, pixel) {
    const expect = (ok, passed, error) => ok ? { passed } : { error: `${error}: ${pixel}` };
    if (forwardPlus && phase === 4)
        return expect(pixel[0] >= 0.5 * 255 && pixel[0] > pixel[2] + 0.2 * 255, 'BROWSER_VOLUMETRIC_FOG_PASSED',
            'Volumetric fog did not produce the expected red background');
    if (forwardPlus && phase === 5)
        return expect(pixel[2] >= 0.025 * 255 && pixel[2] > pixel[0] + 0.01 * 255, 'BROWSER_SDFGI_INDIRECT_LIGHT_PASSED',
            'SDFGI did not transfer blue emission to the unlit target');
    if (phase === 4)
        return expect(pixel[0] >= 0.5 * 255 && pixel[0] > pixel[2] + 0.2 * 255, 'BROWSER_DEPTH_FOG_PASSED',
            'Depth fog did not produce the expected red target');
    if (phase === 5)
        return expect(pixel[2] >= 0.025 * 255 && pixel[2] > pixel[0] + 0.01 * 255, 'BROWSER_AMBIENT_LIGHT_PASSED',
            'Ambient light did not produce the expected blue target');
    if (phase === 6)
        return expect(pixel[0] >= 0.5 * 255, 'BROWSER_MSAA_PIXELS_PASSED', 'MSAA lost the lit target');
    if (phase === 7)
        return expect(pixel[0] >= 0.5 * 255 && pixel[2] >= 0.5 * 255 && pixel[1] <= 0.1 * 255,
            'BROWSER_USER_VARYINGS_PASSED', 'User varyings did not produce a magenta target');
    if (phase === 8)
        return expect(pixel[1] >= 0.2 * 255 && pixel[1] > pixel[0] + 0.15 * 255, 'BROWSER_LIGHTMAP_BINDING_BUDGET_PASSED',
            'The textured lightmapped material did not produce a green target');
    if (phase === 9)
        return expect(pixel[0] >= 0.5 * 255 && pixel[1] >= 0.5 * 255 && pixel[2] <= 0.1 * 255,
            'BROWSER_POINT_COORDINATES_PASSED', 'Point coordinates did not produce a yellow target');
    if (phase === 10)
        return expect(pixel[0] >= 0.1 * 255, 'BROWSER_DEPTH_TEXTURE_PASSED', 'Depth texture returned the zero fallback');
    if (phase === 11)
        return expect(pixel[0] >= 0.8 * 255 && pixel[1] >= 0.3 * 255 && pixel[1] <= 0.8 * 255 && pixel[2] <= 0.1 * 255,
            'BROWSER_DXT5_RA_AS_RG_PASSED', 'DXT5 RA_AS_RG lost its channels');
    if (phase === 12)
        return expect(pixel[1] >= 0.3 * 255 && pixel[0] <= 0.2 * 255 && pixel[2] <= 0.1 * 255,
            'BROWSER_ETC2_RA_AS_RG_PASSED', 'ETC2 RA_AS_RG lost its channels');
}
