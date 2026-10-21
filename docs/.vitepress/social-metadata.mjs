import { join } from 'node:path';
import { createSocialImageGenerator, socialImageWidth, socialImageHeight } from './social-images.mjs';

// Use the rendered article so illustration URLs include Vite's asset hashes.
// Social crawlers read the generated HTML without running the site's JavaScript.
export const siteUrl = 'https://2dog.dev';

// Card eyebrows name the page's part of the docs; the homepage has none and other pages are guides.
const sections = [
  [/^index\.md$/, ''],
  [/^api(?:\/|-reference\.md$)/, 'API Reference'],
  [/^(?:cli\/|dnx-2dog\.md$)/, 'CLI'],
  [/^(?:configuration|build-configurations)\b/, 'Configuration'],
  [/^hosts\//, 'Hosts'],
  [/^known-issues\//, 'Known Issues'],
  [/^testing\//, 'Testing'],
  [/^(?:faq|troubleshooting)\.md$/, 'Help']
];

function attribute(tag, name) {
  const value = tag?.match(new RegExp(`\\s${name}="([^"]*)"`))?.[1];
  const entities = { amp: '&', quot: '"', apos: "'", '#39': "'", lt: '<', gt: '>' };
  return value?.replace(/&(amp|quot|apos|#39|lt|gt);/g, (_, entity) => entities[entity]);
}

/**
 * @param {(illustration: string | undefined, outDir: string, copy: { section: string, title: string, tagline: string }) => Promise<string>} generateImage
 * @returns {(context: import('vitepress').TransformContext) => Promise<import('vitepress').HeadConfig[]>}
 */
export function createSocialMetadata(generateImage) {
  return async function ({ pageData, siteData, siteConfig, title, description, content }) {
    const path = pageData.relativePath
      .replace(/(^|\/)index\.md$/, '$1')
      .replace(/\.md$/, siteData.cleanUrls ? '' : '.html');
    const url = new URL(`${siteData.base}${path}`, siteUrl).href;

    // Limit the image search to the article: the navbar's logo comes first in HTML.
    // Pages without local artwork use the default Lucky and Dogbot illustration.
    const article = content.match(/<main\b[^>]*class="main"[^>]*>([\s\S]*?)<\/main>/)?.[1];
    const illustration = article?.match(/<img\b[^>]*>/)?.[0];
    const src = attribute(illustration, 'src');
    const sourceUrl = src && !src.startsWith('data:') ? new URL(src, url) : undefined;
    const hasIllustration = sourceUrl?.origin === siteUrl && sourceUrl.pathname.startsWith(siteData.base);
    const illustrationPath = hasIllustration
      ? join(siteConfig.outDir, decodeURIComponent(sourceUrl.pathname.slice(siteData.base.length)))
      : undefined;
    const caption = pageData.title || title;
    const tagline = pageData.frontmatter?.tagline || description;
    const section = sections.find(([pattern]) => pattern.test(pageData.relativePath))?.[1] ?? 'Guide';
    const imagePath = await generateImage(illustrationPath, siteConfig.outDir, { section, title: caption, tagline });
    const image = new URL(`${siteData.base}${imagePath}`, siteUrl).href;
    const imageAlt = `${caption}. ${tagline} Read the dogs! 2dog logo with Lucky and Dogbot.`;

    return [
      ['link', { rel: 'canonical', href: url }],
      ['meta', { property: 'og:site_name', content: '2dog' }],
      ['meta', { property: 'og:type', content: 'website' }],
      ['meta', { property: 'og:locale', content: siteData.lang.replace(/-/g, '_') }],
      ['meta', { property: 'og:url', content: url }],
      ['meta', { property: 'og:title', content: title }],
      ['meta', { property: 'og:description', content: description }],
      ['meta', { property: 'og:image', content: image }],
      ['meta', { property: 'og:image:type', content: 'image/png' }],
      ['meta', { property: 'og:image:width', content: String(socialImageWidth) }],
      ['meta', { property: 'og:image:height', content: String(socialImageHeight) }],
      ['meta', { property: 'og:image:alt', content: imageAlt }],
      ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
      ['meta', { name: 'twitter:title', content: title }],
      ['meta', { name: 'twitter:description', content: description }],
      ['meta', { name: 'twitter:image', content: image }],
      ['meta', { name: 'twitter:image:alt', content: imageAlt }]
    ];
  };
}

export const socialMetadata = createSocialMetadata(createSocialImageGenerator());
