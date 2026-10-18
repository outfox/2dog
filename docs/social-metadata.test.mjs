import { test } from 'node:test';
import assert from 'node:assert/strict';
import { socialMetadata } from './.vitepress/social-metadata.mjs';

function metadata(overrides = {}) {
  return socialMetadata({
    pageData: { relativePath: 'hosts/web.md' },
    siteData: { base: '/', lang: 'en-US', cleanUrls: false },
    title: 'Web (HTML5) 🦴 2dog',
    description: 'Run Godot C# in the browser.',
    content: '<main class="main"><h1>Web</h1></main>',
    ...overrides
  });
}

function value(head, key) {
  return head.find(([, attrs]) => (attrs.property || attrs.name || attrs.rel) === key)?.[1];
}

test('share titles and descriptions match the resolved page, including its title template', () => {
  const head = metadata();
  for (const key of ['og:title', 'twitter:title']) {
    assert.equal(value(head, key).content, 'Web (HTML5) 🦴 2dog');
  }
  for (const key of ['og:description', 'twitter:description']) {
    assert.equal(value(head, key).content, 'Run Godot C# in the browser.');
  }
  assert.equal(value(head, 'og:locale').content, 'en_US');
});

test('canonical and Open Graph URLs follow the deployed HTML and directory routes', () => {
  for (const [path, expected] of [
    ['index.md', '/'],
    ['getting-started.md', '/getting-started.html'],
    ['hosts/web.md', '/hosts/web.html'],
    ['hosts/index.md', '/hosts/']
  ]) {
    const head = metadata({ pageData: { relativePath: path } });
    assert.equal(value(head, 'canonical').href, `https://2dog.dev${expected}`);
    assert.equal(value(head, 'og:url').content, `https://2dog.dev${expected}`);
  }
});

test('URLs respect clean URLs and a deployment subpath', () => {
  const head = metadata({ siteData: { base: '/docs/', lang: 'en-US', cleanUrls: true } });
  assert.equal(value(head, 'og:url').content, 'https://2dog.dev/docs/hosts/web');
});

test('uses the article illustration with its final asset URL and decoded alt text', () => {
  const head = metadata({
    content: '<nav><img src="/logo.svg" alt="Logo"></nav>' +
      '<main class="main"><img alt="Lucky &amp; Dogbot &quot;walkies&quot;" ' +
      'src="/assets/2dog-walkies.ABC123.png"></main>'
  });
  for (const key of ['og:image', 'twitter:image']) {
    assert.equal(value(head, key).content, 'https://2dog.dev/assets/2dog-walkies.ABC123.png');
  }
  for (const key of ['og:image:alt', 'twitter:image:alt']) {
    assert.equal(value(head, key).content, 'Lucky & Dogbot "walkies"');
  }
});

test('uses the shared image for the homepage, pages without illustrations, and inline images', () => {
  for (const content of [
    '<main class="home-page"><img src="/logo.svg"></main>',
    '<nav><img src="/logo.svg"></nav><main class="main"><h1>API</h1></main>',
    '<main class="main"><img src="data:image/png;base64,ABC" alt="Inline"></main>'
  ]) {
    const head = metadata({ content });
    assert.equal(value(head, 'og:image').content, 'https://2dog.dev/og-image.png');
    assert.equal(value(head, 'twitter:image').content, 'https://2dog.dev/og-image.png');
  }
});
