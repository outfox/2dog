// Use the rendered article so illustration URLs include Vite's asset hashes.
// Social crawlers read the generated HTML without running the site's JavaScript.
export const siteUrl = 'https://2dog.dev';

function attribute(tag, name) {
  const value = tag?.match(new RegExp(`\\s${name}="([^"]*)"`))?.[1];
  const entities = { amp: '&', quot: '"', apos: "'", '#39': "'", lt: '<', gt: '>' };
  return value?.replace(/&(amp|quot|apos|#39|lt|gt);/g, (_, entity) => entities[entity]);
}

/**
 * @param {import('vitepress').TransformContext} context
 * @returns {import('vitepress').HeadConfig[]}
 */
export function socialMetadata({ pageData, siteData, title, description, content }) {
  const path = pageData.relativePath
    .replace(/(^|\/)index\.md$/, '$1')
    .replace(/\.md$/, siteData.cleanUrls ? '' : '.html');
  const url = new URL(`${siteData.base}${path}`, siteUrl).href;

  // Limit the image search to the article: the navbar's logo comes first in HTML.
  // The custom homepage uses its existing, full-sized social image as the fallback.
  const article = content.match(/<main\b[^>]*class="main"[^>]*>([\s\S]*?)<\/main>/)?.[1];
  const illustration = article?.match(/<img\b[^>]*>/)?.[0];
  const src = attribute(illustration, 'src');
  const hasIllustration = src && !src.startsWith('data:');
  const image = hasIllustration ? new URL(src, url).href : `${siteUrl}/og-image.png`;
  const imageAlt = hasIllustration
    ? attribute(illustration, 'alt') || title
    : '2dog logo - a happy white dog';

  return [
    ['link', { rel: 'canonical', href: url }],
    ['meta', { property: 'og:site_name', content: '2dog' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:locale', content: siteData.lang.replace(/-/g, '_') }],
    ['meta', { property: 'og:url', content: url }],
    ['meta', { property: 'og:title', content: title }],
    ['meta', { property: 'og:description', content: description }],
    ['meta', { property: 'og:image', content: image }],
    ['meta', { property: 'og:image:alt', content: imageAlt }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:title', content: title }],
    ['meta', { name: 'twitter:description', content: description }],
    ['meta', { name: 'twitter:image', content: image }],
    ['meta', { name: 'twitter:image:alt', content: imageAlt }]
  ];
}
