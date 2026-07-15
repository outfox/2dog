#!/usr/bin/env node

const [targetUrl, timeoutValue = "120000", portValue = "9222"] = process.argv.slice(2);

if (!targetUrl) {
    console.error("Usage: wait-for-browser-smoke.mjs <url> [timeout-ms] [debug-port]");
    process.exit(2);
}

const timeoutMs = Number.parseInt(timeoutValue, 10);
const debugPort = Number.parseInt(portValue, 10);

if (!Number.isFinite(timeoutMs) || timeoutMs <= 0 || !Number.isFinite(debugPort) || debugPort <= 0) {
    console.error("The timeout and debug port must be positive integers.");
    process.exit(2);
}

const deadline = Date.now() + timeoutMs;
const devToolsUrl = `http://127.0.0.1:${debugPort}`;
const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
const debugProtocol = process.env.TWODOG_CDP_DEBUG === "1";

async function findPageTarget() {
    let lastError;

    while (Date.now() < deadline) {
        try {
            const response = await fetch(`${devToolsUrl}/json/list`);
            if (!response.ok) {
                throw new Error(`DevTools target request returned HTTP ${response.status}`);
            }

            const targets = await response.json();
            const page = targets.find((target) =>
                target.type === "page" && target.url.startsWith(targetUrl));

            if (page?.webSocketDebuggerUrl) {
                return page;
            }
        } catch (error) {
            lastError = error;
        }

        await sleep(100);
    }

    throw new Error(`Chrome did not expose ${targetUrl} through DevTools: ${lastError ?? "timed out"}`);
}

function formatRemoteObject(object) {
    if (Object.hasOwn(object, "value")) {
        return typeof object.value === "string" ? object.value : JSON.stringify(object.value);
    }

    return object.description ?? object.type;
}

async function run() {
    const page = await findPageTarget();
    const socket = new WebSocket(page.webSocketDebuggerUrl);
    const pending = new Map();
    let commandId = 0;
    let lastState;

    await new Promise((resolve, reject) => {
        const timer = setTimeout(
            () => reject(new Error("Timed out connecting to the Chrome DevTools socket")),
            Math.max(1, deadline - Date.now()));

        socket.addEventListener("open", () => {
            clearTimeout(timer);
            resolve();
        }, { once: true });
        socket.addEventListener("error", () => {
            clearTimeout(timer);
            reject(new Error("Chrome DevTools WebSocket connection failed"));
        }, { once: true });
    });

    socket.addEventListener("message", (event) => {
        if (debugProtocol) {
            console.error(`[cdp:receive] ${event.data}`);
        }

        const message = JSON.parse(event.data);

        if (message.id) {
            const request = pending.get(message.id);
            if (!request) {
                return;
            }

            pending.delete(message.id);
            clearTimeout(request.timer);

            if (message.error) {
                request.reject(new Error(`${request.method}: ${message.error.message}`));
            } else {
                request.resolve(message.result);
            }

            return;
        }

        if (message.method === "Runtime.consoleAPICalled") {
            const values = message.params.args.map(formatRemoteObject).join(" ");
            console.log(`[browser:${message.params.type}] ${values}`);
        } else if (message.method === "Runtime.exceptionThrown") {
            const details = message.params.exceptionDetails;
            console.error(`[browser:exception] ${details.exception?.description ?? details.text}`);
        } else if (message.method === "Log.entryAdded") {
            const entry = message.params.entry;
            console.log(`[browser:${entry.level}] ${entry.text}`);
        }
    });

    socket.addEventListener("close", () => {
        for (const request of pending.values()) {
            clearTimeout(request.timer);
            request.reject(new Error("Chrome closed the DevTools connection"));
        }
        pending.clear();
    });

    function command(method, params = {}, timeoutOverride) {
        return new Promise((resolve, reject) => {
            const id = ++commandId;
            const remaining = Math.max(1, deadline - Date.now());
            const timer = setTimeout(() => {
                pending.delete(id);
                reject(new Error(`${method} timed out`));
            }, timeoutOverride ?? remaining);

            pending.set(id, { method, resolve, reject, timer });
            const message = JSON.stringify({ id, method, params });
            if (debugProtocol) {
                console.error(`[cdp:send] ${message}`);
            }
            socket.send(message);
        });
    }

    async function evaluate(expression, timeoutOverride) {
        const response = await command("Runtime.evaluate", {
            expression,
            returnByValue: true,
        }, timeoutOverride);

        if (response.exceptionDetails) {
            throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
        }

        return response.result.value;
    }

    async function printDom() {
        try {
            const dom = await evaluate("document.documentElement.outerHTML", 5_000);
            console.error(`Browser DOM at failure:\n${dom}`);
        } catch (error) {
            console.error(`Could not capture the browser DOM: ${error}`);
        }
    }

    try {
        await command("Runtime.enable");
        await command("Log.enable");

        while (Date.now() < deadline) {
            lastState = await evaluate(`(() => {
                const notice = document.getElementById("status-notice");
                return {
                    smoke: document.documentElement.getAttribute("data-twodog-smoke"),
                    readyState: document.readyState,
                    failureNotice: notice?.style.display === "block" ? notice.textContent : "",
                };
            })()`);

            if (lastState.smoke === "passed") {
                console.log("Browser reported data-twodog-smoke=passed");
                return;
            }

            if (lastState.smoke === "failed") {
                throw new Error("The browser app reported data-twodog-smoke=failed");
            }

            if (lastState.failureNotice) {
                throw new Error(`Godot browser startup failed: ${lastState.failureNotice}`);
            }

            await sleep(250);
        }

        throw new Error(`Timed out waiting for the browser smoke result; last state: ${JSON.stringify(lastState)}`);
    } catch (error) {
        await printDom();
        throw error;
    } finally {
        socket.close();
    }
}

try {
    await run();
} catch (error) {
    console.error(`Browser smoke probe failed: ${error.stack ?? error}`);
    process.exitCode = 1;
}
