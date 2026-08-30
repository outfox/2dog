#!/usr/bin/env node
// Drives the Blazor showcase through a second engine lifetime: waits for the first smoke pass, clicks
// "Quit engine", waits for the quit to complete, clicks "Start engine", then waits for the smoke marker of
// lifetime 2. Usage: browser-restart-smoke.mjs <url> [timeout-ms] [debug-port]

const [targetUrl, timeoutValue = "120000", portValue = "9222"] = process.argv.slice(2);
if (!targetUrl) {
    console.error("Usage: browser-restart-smoke.mjs <url> [timeout-ms] [debug-port]");
    process.exit(2);
}
const timeoutMs = Number.parseInt(timeoutValue, 10);
const debugPort = Number.parseInt(portValue, 10);
const deadline = Date.now() + timeoutMs;
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const targets = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
const page = targets.find((t) => t.type === "page" && t.url.startsWith(targetUrl));
if (!page?.webSocketDebuggerUrl) {
    console.error(`Chrome does not expose ${targetUrl} through DevTools`);
    process.exit(1);
}

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { ws.addEventListener("open", resolve); ws.addEventListener("error", reject); });
let nextId = 0;
const pending = new Map();
ws.addEventListener("message", (message) => {
    const data = JSON.parse(message.data);
    if (data.id && pending.has(data.id)) {
        pending.get(data.id)(data);
        pending.delete(data.id);
    } else if (data.method === "Runtime.consoleAPICalled") {
        const text = data.params.args.map((a) => a.value ?? a.description ?? "").join(" ");
        if (/error|exception|2DOG|Engine:|destroyed/i.test(text)) console.log(`[browser:${data.params.type}] ${text.slice(0, 300)}`);
    } else if (data.method === "Runtime.exceptionThrown") {
        console.error(`[browser:exception] ${data.params.exceptionDetails.exception?.description ?? data.params.exceptionDetails.text}`);
    }
});
const send = (method, params = {}) => new Promise((resolve) => {
    const id = ++nextId;
    pending.set(id, resolve);
    ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async (expression) => {
    const result = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
    if (result.result?.exceptionDetails) throw new Error(result.result.exceptionDetails.text);
    return result.result?.result?.value;
};
const state = () => evaluate(`JSON.stringify({
    smoke: document.documentElement.getAttribute("data-twodog-smoke"),
    lifetime: document.documentElement.getAttribute("data-twodog-lifetime"),
    status: document.querySelector(".status")?.textContent ?? null,
})`).then(JSON.parse);
const click = (label) => evaluate(`(() => {
    const button = [...document.querySelectorAll("button")].find((b) => b.textContent.trim() === ${JSON.stringify(label)});
    if (!button) return "missing";
    if (button.disabled) return "disabled";
    button.click();
    return "clicked";
})()`);
const waitFor = async (what, predicate) => {
    let last = null;
    while (Date.now() < deadline) {
        last = await state();
        if (predicate(last)) {
            console.log(`${what}: ${JSON.stringify(last)}`);
            return last;
        }
        await sleep(250);
    }
    throw new Error(`Timed out waiting for ${what}; last state: ${JSON.stringify(last)}`);
};

try {
    await send("Runtime.enable");
    await waitFor("first lifetime", (s) => s.smoke === "passed" && s.lifetime === "1");
    await sleep(1000);
    const quit = await click("Quit engine");
    if (quit !== "clicked") throw new Error(`Quit engine button: ${quit}`);
    await waitFor("engine quit", (s) => s.status?.startsWith("Godot quit"));
    await sleep(500);
    const start = await click("Start engine");
    if (start !== "clicked") throw new Error(`Start engine button: ${start}`);
    await waitFor("second lifetime", (s) => s.smoke === "passed" && s.lifetime === "2" && s.status === "Running");
    console.log("Engine restart smoke passed");
    ws.close();
    process.exit(0);
} catch (error) {
    console.error(`Engine restart smoke failed: ${error.message}`);
    ws.close();
    process.exit(1);
}
