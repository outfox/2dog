// 2dog.blazor: prepares the .NET runtime Blazor booted for Godot, which the C# GodotView then starts in-process.
// Mirrors what Godot's engine.js does before callMain: file system init, pack preload, canvas/config handoff.

const observers = new WeakMap();
let fsReady = false;

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

    if (!fsReady) {
        await Module.initFS(['/userfs']);
        fsReady = true;
    }

    const response = await fetch(resolveUrl(options.packUrl));
    if (!response.ok) {
        throw new Error(`2dog.blazor: could not load the game pack '${options.packUrl}' (HTTP ${response.status}).`);
    }
    Module.copyToFS(options.packName, await response.arrayBuffer());

    if (canvas.tabIndex < 0) {
        canvas.tabIndex = 0;
    }
    release(canvas);
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
    observers.get(canvas)?.disconnect();
    observers.delete(canvas);
}
