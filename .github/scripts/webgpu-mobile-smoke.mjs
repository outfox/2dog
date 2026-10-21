import { setTimeout as delay } from 'node:timers/promises';
import { mkdir, writeFile } from 'node:fs/promises';

const [url, port = '9233', timeout = '240000', output] = process.argv.slice(2);
if (!url) throw new Error('Usage: webgpu-mobile-smoke.mjs <url> [CDP port] [timeout ms] [screenshot directory]');
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
        if (message.params.type === 'error' || message.params.type === 'warning') errors.push(text);
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
    await send('Page.navigate', { url });
    if (output) await mkdir(output, { recursive: true });
    const samples = [];
    for (let phase = 0; phase < 9; phase++) {
        if (phase) await evaluate(`document.documentElement.dataset.requestPhase='${phase}'`);
        await waitFor(`document.documentElement?.dataset.readyPhase==='${phase}'`);
        await delay(200);
        const clip = await evaluate(`(() => {
            const r = document.querySelector('canvas').getBoundingClientRect();
            return { x: r.x, y: r.y, width: r.width, height: r.height, scale: 1 };
        })()`);
        const shot = await send('Page.captureScreenshot', { format: 'png', clip });
        if (output) await writeFile(`${output}/phase-${phase}.png`, Buffer.from(shot.data, 'base64'));
        const pixel = await evaluate(`(async () => {
            const blob = await (await fetch('data:image/png;base64,${shot.data}')).blob();
            const bitmap = await createImageBitmap(blob);
            const canvas = document.createElement('canvas');
            canvas.width = bitmap.width;
            canvas.height = bitmap.height;
            const context = canvas.getContext('2d');
            context.drawImage(bitmap, 0, 0);
            return [...context.getImageData(canvas.width / 2, canvas.height / 2, 1, 1).data];
        })()`);
        console.log('BROWSER_MOBILE_PIXEL', phase, pixel);
        if (phase === 4) {
            if (pixel[0] < 0.5 * 255 || pixel[0] <= pixel[2] + 0.2 * 255)
                throw new Error(`Depth fog did not produce the expected red target: ${pixel}`);
            console.log('BROWSER_DEPTH_FOG_PASSED');
        } else if (phase === 5) {
            if (pixel[2] < 0.025 * 255 || pixel[2] <= pixel[0] + 0.01 * 255)
                throw new Error(`Ambient light did not produce the expected blue target: ${pixel}`);
            console.log('BROWSER_AMBIENT_LIGHT_PASSED');
        } else if (phase === 6) {
            if (pixel[0] < 0.5 * 255) throw new Error(`MSAA lost the lit target: ${pixel}`);
            console.log('BROWSER_MSAA_PIXELS_PASSED');
        } else if (phase === 7) {
            if (pixel[0] < 0.5 * 255 || pixel[2] < 0.5 * 255 || pixel[1] > 0.1 * 255)
                throw new Error(`User varyings did not produce a magenta target: ${pixel}`);
            console.log('BROWSER_USER_VARYINGS_PASSED');
        } else if (phase === 8) {
            if (pixel[1] < 0.2 * 255 || pixel[1] <= pixel[0] + 0.15 * 255)
                throw new Error(`The textured lightmapped material did not produce a green target: ${pixel}`);
            console.log('BROWSER_LIGHTMAP_BINDING_BUDGET_PASSED');
        } else {
            samples.push((pixel[0] + pixel[1] + pixel[2]) / (3 * 255));
        }
    }
    await evaluate(`document.documentElement.dataset.samples='${samples.join(',')}';document.documentElement.dataset.requestPhase='9'`);
    await waitFor(`document.documentElement?.dataset.twodogSmoke==='passed'`);
    if (errors.length) throw new Error(errors.join('\n'));
    console.log('BROWSER_MOBILE_LIGHT_LIST_PASSED', samples);
} finally {
    socket.close();
    await fetch(`${cdp}/json/close/${target.id}`);
}
