// Generates content/public/llms.txt and content/public/llms-full.txt
// (https://llmstxt.org) from the site's markdown sources, using each page's
// frontmatter title and description. Sections mirror the sidebar. Runs
// automatically before `npm run dev` / `npm run build`.
import { readFileSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { loadPages } from './llms-pages.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const content = join(here, 'content');
const HOST = 'https://2dog.dev';

// Same version source as .vitepress/plugins/version-markers.mts: the repo-root
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
// listed here or in EXCLUDE. Missing metadata, unlisted pages and stale entries
// fail validation, including dev/build, so published indexes cannot omit pages.
const SECTIONS = [
  ['Start Here', [
    'getting-started.md', 'concepts.md', 'project-layout.md',
  ]],
  ['Hosts', [
    'hosts/index.md', 'hosts/generic.md', 'hosts/android.md', 'hosts/avalonia.md', 'hosts/blazor.md',
    'hosts/nunit.md', 'hosts/repl.md', 'hosts/web.md', 'hosts/webxr.md', 'hosts/winforms.md',
    'hosts/winui.md', 'hosts/xunit.md',
  ]],
  ['API Reference', [
    'api-reference.md', 'api/engine.md', 'api/godot-instance.md', 'api/godotsharp.md',
    'api/fixture-base.md', 'api/fixture.md', 'api/headless-fixture.md',
    'api/rendering-collection.md', 'api/headless-collection.md', 'api/assembly-preloader.md',
  ]],
  ['Develop and Configure', [
    'dnx-2dog.md', 'cli/add.md', 'cli/new.md', 'cli/doctor.md', 'cli/update.md', 'cli/pack.md',
    'cli/pinning.md', 'cli/version.md', 'cli/help.md', 'import-tool.md', 'testing.md', 'build-configurations.md',
  ]],
  ['MSBuild Configuration', [
    'configuration.md', 'configuration/generic.md', 'configuration/android.md',
    'configuration/avalonia.md', 'configuration/blazor.md', 'configuration/nunit.md', 'configuration/repl.md',
    'configuration/web.md',
    'configuration/webxr.md', 'configuration/winforms.md', 'configuration/winui.md',
    'configuration/xunit.md',
  ]],
  ['Known Issues', [
    'known-issues/index.md', 'known-issues/single-instance.md',
    'known-issues/xunit-discovery.md', 'known-issues/gd-print-output.md',
    'known-issues/spaced-project-names.md',
  ]],
  ['Optional', [
    'faq.md', 'troubleshooting.md',
  ]],
];

// Redirect stubs and other pages that should not be indexed.
const EXCLUDE = new Set(['add.md', 'templates.md', 'convert.md', 'web.md']);

const inventory = loadPages(content, SECTIONS, EXCLUDE);
const prepare = (page) => ({ ...page, url: `${HOST}/${page.rel}`, body: resolveMarkers(page.body) });
const home = prepare(inventory.home);
const sections = inventory.sections.map(({ label, pages }) => ({ label, pages: pages.map(prepare) }));
const pageCount = 1 + sections.reduce((n, s) => n + s.pages.length, 0);
if (process.argv.includes('--check')) {
  console.log(`make-llms: ${pageCount} pages validated`);
  process.exit(0);
}
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
