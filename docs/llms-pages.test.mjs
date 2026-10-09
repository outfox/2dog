import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { loadPages } from './llms-pages.mjs';

function fixture(t) {
  const dir = mkdtempSync(join(tmpdir(), '2dog-docs-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const write = (rel, text = '---\ntitle: Page\ndescription: A page\n---\nBody\n') => {
    const path = join(dir, rel);
    mkdirSync(join(path, '..'), { recursive: true });
    writeFileSync(path, text);
  };
  write('index.md');
  write('guide.md');
  return { dir, write, sections: [['Guides', ['guide.md']]] };
}

test('indexes every authored page, ignoring public assets and redirect stubs', (t) => {
  const { dir, write, sections } = fixture(t);
  write('public/icons/README.md', 'vendored readme');
  write('redirect.md', 'redirect');
  const result = loadPages(dir, sections, new Set(['redirect.md']));
  assert.equal(result.home.rel, 'index.md');
  assert.deepEqual(result.sections[0].pages.map(p => p.rel), ['guide.md']);
  assert.equal(result.sections[0].pages[0].body, 'Body\n');
});

test('new pages must be classified', (t) => {
  const { dir, write, sections } = fixture(t);
  write('new-page.md');
  assert.throws(() => loadPages(dir, sections), /new-page.md is not listed/);
});

test('removed pages must be removed from the index', (t) => {
  const { dir } = fixture(t);
  assert.throws(() => loadPages(dir, [['Guides', ['guide.md', 'missing.md']]]), /missing.md is listed.*missing/);
});

test('missing metadata fails instead of silently omitting a page', (t) => {
  const { dir, write, sections } = fixture(t);
  write('guide.md', '---\ntitle: Guide\n---\nBody\n');
  assert.throws(() => loadPages(dir, sections), /guide.md has no frontmatter title\/description/);
});

test('duplicate index entries fail', (t) => {
  const { dir } = fixture(t);
  assert.throws(() => loadPages(dir, [['One', ['guide.md']], ['Two', ['guide.md']]]), /guide.md is listed more than once/);
});

test('home metadata is required too', (t) => {
  const { dir, write, sections } = fixture(t);
  write('index.md', '# Home');
  assert.throws(() => loadPages(dir, sections), /index.md has no frontmatter/);
});
