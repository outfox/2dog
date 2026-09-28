// 2dog.blazor: prepares the .NET runtime Blazor booted for Godot, which the C# GodotView then starts in-process.
// Mirrors what Godot's engine.js does before callMain: file system init, pack preload, canvas/config handoff.

const observers = new WeakMap();
const preparations = new WeakMap();
let fsInitialization;

function runtimeModule() {
    const runtime = globalThis.Blazor?.runtime
        ?? (typeof globalThis.getDotnetRuntime === 'function' ? globalThis.getDotnetRuntime(0) : null);
    const module = runtime?.Module;
    if (!module) {
        throw new Error('2dog.blazor: the .NET WebAssembly runtime is not running in this page.');
    }
    if (typeof module.initConfig !== 'function' || typeof module.copyToFS !== 'function') {
        throw new Error('2dog.blazor: the .NET runtime was linked without Godot. Reference 2dog.browser-wasm '
            + 'from the Blazor WebAssembly (client) project and rebuild.');
    }
    return module;
}

function resolveUrl(url) {
    return new URL(url, document.baseURI).href;
}

// Like Godot's own preloader (engine.js retries every download), a transient failure must not fail the start:
// CI restarts saw the pack fetch cancelled at the network layer (net::ERR_ABORTED) while the server served it fine.
const PACK_ATTEMPTS = 4;
const PACK_RETRY_DELAY_MS = 500;

function delay(ms, signal) {
    return new Promise((resolve, reject) => {
        const onAbort = () => {
            clearTimeout(timer);
            reject(signal.reason);
        };
        const timer = setTimeout(() => {
            signal.removeEventListener('abort', onAbort);
            resolve();
        }, ms);
        signal.addEventListener('abort', onAbort, { once: true });
    });
}

async function fetchPack(url, signal) {
    for (let attempt = 1; ; attempt++) {
        let failure;
        let retryable = true;
        try {
            const response = await fetch(url, { signal });
            if (response.ok) {
                return await response.arrayBuffer();
            }
            failure = new Error(`2dog.blazor: could not load the game pack '${url}' (HTTP ${response.status}).`);
            retryable = response.status >= 500;
        } catch (error) {
            // A released view reports its cancellation; network failures reject with a TypeError.
            signal.throwIfAborted();
            if (!(error instanceof TypeError)) {
                throw error;
            }
            failure = error;
        }
        if (!retryable || attempt >= PACK_ATTEMPTS) {
            throw failure;
        }
        console.warn(`2dog.blazor: loading '${url}' failed (${failure.message}); retrying.`);
        await delay(PACK_RETRY_DELAY_MS, signal);
    }
}

// Container mode: the canvas backing store follows its CSS box (Godot's policy 0 reads canvas.width/height).
function observeContainer(canvas) {
    const apply = () => {
        const scale = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.floor(canvas.clientWidth * scale));
        const height = Math.max(1, Math.floor(canvas.clientHeight * scale));
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
    };
    apply();
    const observer = new ResizeObserver(apply);
    observer.observe(canvas);
    observers.set(canvas, observer);
}

/**
 * @param {HTMLCanvasElement} canvas
 * @param {{packUrl: string, packName: string, resize: number, focusCanvas: boolean, locale: ?string}} options
 */
export async function prepare(canvas, options) {
    const Module = runtimeModule();
    release(canvas);
    const controller = new AbortController();
    preparations.set(canvas, controller);
    // Godot fetches its audio worklets through Module.locateFile, which the .NET loader aims at _framework/;
    // the 2dog build publishes them at the site root, where Blazor may have fingerprinted them - import.meta.resolve
    // applies the page's import map (a plain fetch would miss it).
    if (!Module.__twodogLocateFile) {
        const previousLocateFile = Module.locateFile;
        Module.locateFile = (path, directory) => {
            if (path.startsWith('godot.')) {
                return import.meta.resolve(resolveUrl(path));
            }
            return previousLocateFile ? previousLocateFile(path, directory) : `${directory ?? ''}${path}`;
        };
        Module.__twodogLocateFile = true;
    }

    try {
        // An interop cancellation can release the lease while initFS is still running.
        // The next view must join that initialization instead of mounting IDBFS twice.
        await (fsInitialization ??= Module.initFS(['/userfs']).catch((error) => {
            fsInitialization = undefined;
            throw error;
        }));
        controller.signal.throwIfAborted();
        const pack = await fetchPack(resolveUrl(options.packUrl), controller.signal);
        // Cancelling .NET JS interop does not cancel this JavaScript promise. A released
        // view must never reconfigure the runtime after another view acquires the lease.
        controller.signal.throwIfAborted();
        Module.copyToFS(options.packName, pack);
    } finally {
        if (preparations.get(canvas) === controller) {
            preparations.delete(canvas);
        }
    }

    if (canvas.tabIndex < 0) {
        canvas.tabIndex = 0;
    }
    if (options.resize === 0) {
        observeContainer(canvas);
    }

    let locale = options.locale || (navigator.languages ? navigator.languages[0] : navigator.language) || 'en';
    locale = locale.split('.')[0].replace('-', '_');

    Module.initConfig({
        'canvas': canvas,
        'canvasResizePolicy': options.resize,
        'locale': locale,
        'persistentDrops': false,
        'virtualKeyboard': false,
        'godotPoolSize': 4,
        'focusCanvas': !!options.focusCanvas,
        'onExecute': null,
        'onExit': null,
    });
}

export function release(canvas) {
    preparations.get(canvas)?.abort();
    preparations.delete(canvas);
    observers.get(canvas)?.disconnect();
    observers.delete(canvas);
}
