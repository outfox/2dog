import { readFileSync, readdirSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

function frontmatter(file) {
  const text = readFileSync(file, 'utf8').replace(/^\uFEFF/, '');
  const match = text.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
  if (!match) return {};
  const get = (field) => {
    const m = match[1].match(new RegExp(`^${field}: (?:'(.*)'|"(.*)"|(.+))$`, 'm'));
    return m ? (m[1] ?? m[2] ?? m[3]).replace(/''/g, "'").trim() : undefined;
  };
  return { title: get('title'), description: get('description'), body: text.slice(match[0].length) };
}

// All authored pages must have metadata and exactly one index entry (or an explicit exclusion).
// Keeping this independent of generation lets tests exercise new, removed and malformed pages.
export function loadPages(content, sectionDefinitions, exclude = new Set()) {
  const pages = new Map();
  const errors = [];
  for (const entry of readdirSync(content, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile() || !entry.name.endsWith('.md')) continue;
    const file = join(entry.parentPath, entry.name);
    const rel = relative(content, file).split(sep).join('/');
    if (rel.startsWith('public/') || exclude.has(rel)) continue;
    const page = { rel, ...frontmatter(file) };
    if (!page.title || !page.description)
      errors.push(`${rel} has no frontmatter title/description`);
    pages.set(rel, page);
  }

  const home = pages.get('index.md');
  if (!home) errors.push('index.md is missing');
  pages.delete('index.md');
  const listed = new Set();
  const sections = sectionDefinitions.map(([label, rels]) => ({
    label,
    pages: rels.flatMap((rel) => {
      if (listed.has(rel)) errors.push(`${rel} is listed more than once`);
      listed.add(rel);
      if (!pages.has(rel)) {
        errors.push(`${rel} is listed in SECTIONS but missing or excluded`);
        return [];
      }
      return [pages.get(rel)];
    }),
  }));
  for (const rel of pages.keys())
    if (!listed.has(rel)) errors.push(`${rel} is not listed in SECTIONS`);
  if (errors.length) throw new Error(`make-llms: documentation coverage failed:\n${errors.join('\n')}`);
  return { home, sections };
}
