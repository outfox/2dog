#!/usr/bin/env node
// Static file server with cross-origin isolation (COOP/COEP): SharedArrayBuffer, and so WasmEnableThreads bundles,
// needs it. Usage: serve-isolated.mjs <root> [port]; port 0 picks a free one, printed in the "serving" line.
import { createServer } from 'node:http';
import { createReadStream, statSync } from 'node:fs';
import { extname, join, normalize, resolve, sep } from 'node:path';

const [root = '.', portValue = '8000'] = process.argv.slice(2);
const base = resolve(root);
const types = {
    '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript',
    '.wasm': 'application/wasm', '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png',
    '.ico': 'image/x-icon',
};
const headers = {
    'Cross-Origin-Opener-Policy': 'same-origin',
    'Cross-Origin-Embedder-Policy': 'require-corp',
    'Cache-Control': 'no-store',
};

const server = createServer((request, response) => {
    let file;
    try {
        const path = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
        file = normalize(join(base, path.endsWith('/') ? path + 'index.html' : path));
    } catch {
        response.writeHead(400, headers).end();
        return;
    }
    let stat = null;
    try {
        stat = file.startsWith(base + sep) ? statSync(file) : null;
    } catch {
        // Missing files are a 404 below.
    }
    if (!stat?.isFile()) {
        console.log(`404 ${request.url}`);
        response.writeHead(404, headers).end();
        return;
    }
    response.writeHead(200, {
        ...headers,
        'Content-Type': types[extname(file)] ?? 'application/octet-stream',
        'Content-Length': stat.size,
    });
    createReadStream(file).pipe(response);
});
server.listen(Number(portValue), '127.0.0.1', () =>
    console.log(`serving ${base} on http://127.0.0.1:${server.address().port}`));
