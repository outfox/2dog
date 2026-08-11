// Generates content/public/llms.txt and content/public/llms-full.txt
// (https://llmstxt.org) from the site's markdown sources, using each page's
// frontmatter title and description. Sections mirror the sidebar. Runs
// automatically before `npm run dev` / `npm run build`.
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join, relative, dirname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const content = join(here, 'content');
const HOST = 'https://2dog.dev';

// Same version source as .vitepress/plugins/version-markers.ts: the repo-root
// Directory.Build.props. Markers in page bodies resolve to real versions so
// the llms files read like the rendered site.
const props = readFileSync(join(here, '..', 'Directory.Build.props'), 'utf8');
const msbuildProperty = (name) => {
  const match = props.match(new RegExp(`<${name}>([^<]+)</${name}>`));
  if (!match) throw new Error(`Property <${name}> not found in Directory.Build.props`);
  return match[1].trim();
};
const godotVersion = msbuildProperty('GodotVersion');
const twodogVersion = `${godotVersion}.${msbuildProperty('TwoDogRevision')}`;
const nativesRevision = msbuildProperty('NativesRevision');
const nativesVersion = nativesRevision === '0' ? godotVersion : `${godotVersion}.${nativesRevision}`;

const resolveMarkers = (text) => text
  .replaceAll(':2dog-version:', twodogVersion)
  .replaceAll(':godot-version:', godotVersion)
  .replaceAll(':natives-version:', nativesVersion)
  // :gd-name: / :gd-name@tint: sidebar pictograms render as icons on the
  // site; in plain text they are noise.
  .replace(/ ?:gd-[a-z0-9_-]+(?:@[a-z0-9-]+)?:/g, '');

// Sections mirror the sidebar (.vitepress/config.mts). Every page must be
// listed here or in EXCLUDE; anything else gets a build warning and lands in
// the trailing section so it is never silently dropped.
const SECTIONS = [
  ['Start Here', [
    'getting-started.md', 'concepts.md', 'project-layout.md',
  ]],
  ['Build and Ship', [
    'add.md', 'templates.md', 'web.md',
  ]],
  ['Hosts', [
    'hosts/index.md', 'hosts/generic.md', 'hosts/avalonia.md', 'hosts/web.md',
    'hosts/webxr.md', 'hosts/winforms.md', 'hosts/xunit.md',
  ]],
  ['API Reference', [
    'api-reference.md', 'api/engine.md', 'api/godot-instance.md', 'api/godotsharp.md',
    'api/fixture-base.md', 'api/fixture.md', 'api/headless-fixture.md',
    'api/rendering-collection.md', 'api/headless-collection.md', 'api/assembly-preloader.md',
  ]],
  ['Develop and Configure', [
    'dnx-2dog.md', 'import-tool.md', 'testing.md', 'build-configurations.md', 'configuration.md',
  ]],
  ['Known Issues', [
    'known-issues/index.md', 'known-issues/single-instance.md',
    'known-issues/xunit-discovery.md', 'known-issues/gd-print-output.md',
  ]],
  ['Optional', [
    'faq.md', 'misc.md',
  ]],
];

// Redirect stubs and other pages that should not be indexed.
const EXCLUDE = new Set(['convert.md']);

function frontmatter(file) {
  const text = readFileSync(file, 'utf8').replace(/^﻿/, '');
  const match = text.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
  if (!match) return {};
  const get = (field) => {
    const m = match[1].match(new RegExp(`^${field}: (?:'(.*)'|"(.*)"|(.+))$`, 'm'));
    return m ? (m[1] ?? m[2] ?? m[3]).replace(/''/g, "'").trim() : undefined;
  };
  return { title: get('title'), description: get('description'), body: text.slice(match[0].length) };
}

const pages = new Map();
let home;
for (const entry of readdirSync(content, { recursive: true, withFileTypes: true })) {
  if (!entry.isFile() || !entry.name.endsWith('.md')) continue;
  const file = join(entry.parentPath, entry.name);
  const rel = relative(content, file).split(sep).join('/');
  if (rel.startsWith('public/') || EXCLUDE.has(rel)) continue;
  const { title, description, body } = frontmatter(file);
  if (!title || !description) {
    console.warn(`make-llms: ${rel} has no frontmatter title/description - skipped`);
    continue;
  }
  const page = { rel, url: `${HOST}/${rel}`, title, description, body: resolveMarkers(body) };
  if (rel === 'index.md') home = page;
  else pages.set(rel, page);
}
if (!home) throw new Error('make-llms: content/index.md has no frontmatter title/description');

const sections = SECTIONS.map(([label, rels]) => {
  const listed = rels.flatMap((rel) => {
    if (pages.has(rel)) return [pages.get(rel)];
    console.warn(`make-llms: ${rel} is listed in SECTIONS but missing - skipped`);
    return [];
  });
  rels.forEach((rel) => pages.delete(rel));
  return { label, pages: listed };
});
if (pages.size > 0) {
  for (const rel of pages.keys()) console.warn(`make-llms: ${rel} is not listed in SECTIONS - appended to "${SECTIONS.at(-1)[0]}"`);
  sections.at(-1).pages.push(...pages.values());
}
const pageCount = 1 + sections.reduce((n, s) => n + s.pages.length, 0);

// llms-full.txt: a YAML divider per page (page/section/source/description);
// bodies verbatim.
const divider = (page, label) =>
  `---\npage: ${page.title}\nsection: ${label}\nsource: ${page.url}\ndescription: ${page.description}\n---`;

const fullParts = [
  `<!-- 2dog full documentation - one file, ${pageCount} pages, for 2dog ${twodogVersion} (Godot ${godotVersion}).
Each page begins with a YAML block (page/section/source/description); page content
follows verbatim. Index: ${HOST}/llms.txt -->`,
];
// The home page is all-frontmatter (layout: home renders theme components),
// so pages with empty bodies contribute only their index entry.
const pushPage = (page, label) => {
  if (page.body.trim()) fullParts.push(divider(page, label), page.body.trim());
};
pushPage({ ...home, title: '2dog' }, 'Home');
for (const { label, pages } of sections) {
  for (const page of pages) pushPage(page, label);
}

const list = (pages) => pages.map((p) => `- [${p.title}](${p.url}): ${p.description}`).join('\n');

const llms = `# 2dog

> ${home.description}

2dog is a free & open-source (MIT) toolkit that inverts Godot's ownership
model: instead of the Godot editor exporting your game, a plain .NET
application hosts the engine (libgodot) as a library. Your scenes, scripts,
and GodotSharp C# API stay exactly as they are - you gain \`dotnet run\`,
\`dotnet publish\` to desktop and browser (WebAssembly), real-engine xUnit
tests, and embedding in any .NET app.
Current version: ${twodogVersion} (Godot ${godotVersion}).
NuGet packages: \`2dog\` (CLI tool + templates) - https://www.nuget.org/packages/2dog/,
\`2dog.engine\` (the library), \`2dog.xunit\` (test collections).
Source: https://github.com/outfox/2dog

Every page below is also served as raw Markdown: replace \`.html\` with \`.md\`
(the URLs below already point at the Markdown versions). The entire site is
also available concatenated at ${HOST}/llms-full.txt.

${sections.map(({ label, pages }) => `## ${label}\n\n${list(pages)}`).join('\n\n')}
`;

const full = fullParts.join('\n\n') + '\n';
writeFileSync(join(content, 'public', 'llms.txt'), llms);
writeFileSync(join(content, 'public', 'llms-full.txt'), full);
console.log(`llms.txt: ${pageCount} pages indexed; llms-full.txt: ${(full.length / 1024).toFixed(0)} KiB`);
