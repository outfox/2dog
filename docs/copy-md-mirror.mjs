// Copies the site's markdown sources into .vitepress/dist after the build,
// so every rendered page has a raw-Markdown twin (/web.html -> /web.md) for
// AI agents and other machine readers - the llms.txt index links to these.
// Runs automatically after `npm run build` (see package.json postbuild).
import { readFileSync, writeFileSync, mkdirSync, readdirSync } from 'node:fs';
import { join, relative, dirname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const content = join(here, 'content');
const dist = join(here, '.vitepress', 'dist');

let count = 0;
for (const entry of readdirSync(content, { recursive: true, withFileTypes: true })) {
  if (!entry.isFile() || !entry.name.endsWith('.md')) continue;
  const file = join(entry.parentPath, entry.name);
  const rel = relative(content, file).split(sep).join('/');
  if (rel.startsWith('public/')) continue;
  const target = join(dist, rel);
  mkdirSync(dirname(target), { recursive: true });
  writeFileSync(target, readFileSync(file, 'utf8').replace(/^﻿/, ''));
  count++;
}
console.log(`md mirror: ${count} markdown files copied into dist`);
