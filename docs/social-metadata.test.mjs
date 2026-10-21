import { test } from 'node:test';
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { createSocialMetadata } from './.vitepress/social-metadata.mjs';

const socialMetadata = createSocialMetadata(async () => 'social/composed.ABC123.png');

function metadata(overrides = {}) {
  return socialMetadata({
    pageData: { relativePath: 'hosts/web.md' },
    siteData: { base: '/', lang: 'en-US', cleanUrls: false },
    siteConfig: { outDir: '/docs/dist' },
    title: 'Web (HTML5) | 2dog',
    description: 'Run Godot C# in the browser.',
    content: '<main class="main"><h1>Web</h1></main>',
    ...overrides
  });
}

function value(head, key) {
  return head.find(([, attrs]) => (attrs.property || attrs.name || attrs.rel) === key)?.[1];
}

test('share titles and descriptions match the resolved page, including its title template', async () => {
  const head = await metadata();
  for (const key of ['og:title', 'twitter:title']) {
    assert.equal(value(head, key).content, 'Web (HTML5) | 2dog');
  }
  for (const key of ['og:description', 'twitter:description']) {
    assert.equal(value(head, key).content, 'Run Godot C# in the browser.');
  }
  assert.equal(value(head, 'og:locale').content, 'en_US');
});

test('canonical and Open Graph URLs follow the deployed HTML and directory routes', async () => {
  for (const [path, expected] of [
    ['index.md', '/'],
    ['getting-started.md', '/getting-started.html'],
    ['hosts/web.md', '/hosts/web.html'],
    ['hosts/index.md', '/hosts/']
  ]) {
    const head = await metadata({ pageData: { relativePath: path } });
    assert.equal(value(head, 'canonical').href, `https://2dog.dev${expected}`);
    assert.equal(value(head, 'og:url').content, `https://2dog.dev${expected}`);
  }
});

test('URLs respect clean URLs and a deployment subpath', async () => {
  const head = await metadata({ siteData: { base: '/docs/', lang: 'en-US', cleanUrls: true } });
  assert.equal(value(head, 'og:url').content, 'https://2dog.dev/docs/hosts/web');
  assert.equal(value(head, 'og:image').content, 'https://2dog.dev/docs/social/composed.ABC123.png');
});

test('uses a composed article illustration with readable card text and image dimensions', async () => {
  const head = await metadata({
    content: '<nav><img src="/logo.svg" alt="Logo"></nav>' +
      '<main class="main"><img alt="Lucky &amp; Dogbot &quot;walkies&quot;" ' +
      'src="/assets/2dog-walkies.ABC123.png"></main>'
  });
  for (const key of ['og:image', 'twitter:image']) {
    assert.equal(value(head, key).content, 'https://2dog.dev/social/composed.ABC123.png');
  }
  for (const key of ['og:image:alt', 'twitter:image:alt']) {
    assert.equal(value(head, key).content, 'Web (HTML5) | 2dog. Run Godot C# in the browser. Read the dogs! 2dog logo with Lucky and Dogbot.');
  }
});

test('the card invites with the page tagline while share text keeps the description', async () => {
  const head = await metadata({
    pageData: { relativePath: 'hosts/web.md', title: 'Web', frontmatter: { tagline: 'Learn how to publish to the web.' } }
  });
  assert.equal(value(head, 'og:description').content, 'Run Godot C# in the browser.');
  for (const key of ['og:image:alt', 'twitter:image:alt']) {
    assert.equal(value(head, key).content, 'Web. Learn how to publish to the web. Read the dogs! 2dog logo with Lucky and Dogbot.');
  }
  assert.equal(value(head, 'og:image:type').content, 'image/png');
  assert.equal(value(head, 'og:image:width').content, '1200');
  assert.equal(value(head, 'og:image:height').content, '630');
});

test('uses a composed fallback for the homepage, pages without illustrations, and inline images', async () => {
  for (const content of [
    '<main class="home-page"><img src="/logo.svg" alt=""></main>',
    '<nav><img src="/logo.svg" alt=""></nav><main class="main"><h1>API</h1></main>',
    '<main class="main"><img src="data:image/png;base64,ABC" alt="Inline"></main>'
  ]) {
    const head = await metadata({ content });
    assert.equal(value(head, 'og:image').content, 'https://2dog.dev/social/composed.ABC123.png');
    assert.equal(value(head, 'twitter:image').content, 'https://2dog.dev/social/composed.ABC123.png');
  }
});

test('resolves illustrations to emitted local assets under the deployment base', async () => {
  const calls = [];
  const transform = createSocialMetadata(async (...args) => {
    calls.push(args);
    return 'social/composed.ABC123.png';
  });
  await transform({
    pageData: { relativePath: 'hosts/index.md', title: 'Hosts' },
    siteData: { base: '/docs/', lang: 'en-US' },
    siteConfig: { outDir: '/docs/dist' },
    title: 'Hosts | 2dog', description: 'Host Godot.',
    content: '<main class="main"><img src="/docs/assets/2dog-squirrel.ABC123.png" alt=""></main>'
  });
  assert.deepEqual(calls, [[
    join('/docs/dist', 'assets/2dog-squirrel.ABC123.png'), '/docs/dist',
    { section: 'Hosts', title: 'Hosts', tagline: 'Host Godot.' }
  ]]);
});

test('labels cards with the part of the docs a page belongs to', async () => {
  const sections = [];
  const transform = createSocialMetadata(async (_, __, copy) => {
    sections.push(copy.section);
    return 'social/composed.ABC123.png';
  });
  const paths = [
    'index.md', 'getting-started.md', 'api-reference.md', 'api/engine.md', 'dnx-2dog.md', 'cli/new.md',
    'configuration.md', 'build-configurations.md', 'configuration/web.md', 'hosts/index.md',
    'known-issues/index.md', 'testing/xunit.md', 'faq.md', 'troubleshooting.md'
  ];
  for (const relativePath of paths) {
    await transform({
      pageData: { relativePath },
      siteData: { base: '/', lang: 'en-US' },
      siteConfig: { outDir: '/docs/dist' },
      title: 'Page | 2dog', description: 'A page.',
      content: '<main class="main"></main>'
    });
  }
  assert.deepEqual(sections, [
    '', 'Guide', 'API Reference', 'API Reference', 'CLI', 'CLI',
    'Configuration', 'Configuration', 'Configuration', 'Hosts',
    'Known Issues', 'Testing', 'Help', 'Help'
  ]);
});
