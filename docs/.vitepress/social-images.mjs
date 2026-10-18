import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { basename, extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';
import woff2 from 'wawoff2';

export const socialImageWidth = 1200;
export const socialImageHeight = 630;

const logo = fileURLToPath(new URL('../content/public/logo-static.svg', import.meta.url));
const illustrations = fileURLToPath(new URL('../content/img/', import.meta.url));
const fallback = join(illustrations, '2dog-walkies.png');
const logoWidth = 240;
const logoInset = 48;
const copyLeft = 48;
const copyWidth = 576;
const buttonTop = 516;
const buttonWidth = 264;
const buttonHeight = 64;
const fonts = fileURLToPath(new URL('../content/public/fonts/', import.meta.url));
const fontCache = fileURLToPath(new URL('./cache/social-fonts/', import.meta.url));
const fontFiles = new Map();
const background = Buffer.from(`
<svg xmlns="http://www.w3.org/2000/svg" width="${socialImageWidth}" height="${socialImageHeight}">
  <defs>
    <linearGradient id="background" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#000000"/>
      <stop offset="55%" stop-color="#0c141d"/>
      <stop offset="100%" stop-color="#192b3a"/>
    </linearGradient>
  </defs>
  <rect width="100%" height="100%" fill="url(#background)"/>
</svg>`);

function escapeMarkup(text) {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

// Pango cannot use our WOFF2 files directly. Decode the same vendored fonts into
// the ignored build cache so card typography does not depend on system fonts.
function textFont(bold) {
  const name = bold ? 'Inter_Bold' : 'Inter_Regular';
  if (!fontFiles.has(name)) {
    fontFiles.set(name, (async () => {
      const source = await readFile(join(fonts, `${name}.woff2`));
      const hash = createHash('sha256').update(source).digest('hex').slice(0, 12);
      const path = join(fontCache, `${name}.${hash}.ttf`);
      if (!existsSync(path)) {
        await mkdir(fontCache, { recursive: true });
        const temporary = `${path}.${process.pid}.tmp`;
        await writeFile(temporary, await woff2.decompress(source));
        try {
          await rename(temporary, path);
        } catch (error) {
          if (!existsSync(path)) throw error;
        } finally {
          await rm(temporary, { force: true });
        }
      }
      return path;
    })());
  }
  return fontFiles.get(name);
}

async function renderText(text, size, { bold = false, color = '#f5f8fc', width = copyWidth } = {}) {
  return sharp({ text: {
    text: `<span foreground="${color}">${escapeMarkup(text)}</span>`,
    font: `Inter ${bold ? 'Bold ' : ''}${size}`,
    fontfile: await textFont(bold),
    width,
    wrap: 'word-char',
    spacing: 8,
    rgba: true
  }}).png().toBuffer({ resolveWithObject: true });
}

async function renderCopy(title, summary) {
  const layers = [];
  let heading;
  for (let size = 52; size >= 28; size -= 2) {
    heading = await renderText(title.replace(/\./g, '.\u200b'), size, { bold: true });
    if (heading.info.height <= 168) break;
  }
  if (heading.info.height > 168) throw new Error(`Social image title does not fit: ${title}`);
  layers.push({ input: heading.data, top: 56, left: copyLeft });

  const summaryTop = 56 + heading.info.height + 28;
  const maxSummaryHeight = Math.min(208, buttonTop - summaryTop - 32);
  let excerpt = summary.replace(/\s+/g, ' ').trim();
  while (excerpt) {
    const paragraph = await renderText(excerpt, 26, { color: '#becbd9' });
    if (paragraph.info.height <= maxSummaryHeight) {
      layers.push({ input: paragraph.data, top: summaryTop, left: copyLeft });
      break;
    }
    excerpt = excerpt.replace(/(?:\s+\S+|\S)\.*$/, '').trimEnd() + '...';
  }

  const button = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${buttonWidth}" height="${buttonHeight}">
    <rect x="0.5" y="0.5" width="${buttonWidth - 1}" height="${buttonHeight - 1}" rx="8" fill="#579eff" stroke="#83b8ff"/>
  </svg>`);
  const label = await renderText('Read the dogs!', 26, { bold: true, color: '#0b1522', width: buttonWidth - 32 });
  layers.push(
    { input: button, top: buttonTop, left: copyLeft },
    {
      input: label.data,
      top: buttonTop + Math.floor((buttonHeight - label.info.height) / 2),
      left: copyLeft + Math.floor((buttonWidth - label.info.width) / 2)
    }
  );
  return layers;
}

function preferredIllustration(illustration) {
  if (typeof illustration !== 'string') return illustration;
  // Vite's emitted assets also have a hash: name-big.ABC123.png -> name.png.
  const name = basename(illustration);
  const standardName = name.replace(/-big(?:\.[^.]+)?(?=\.png$)/, '');
  const standard = join(illustrations, standardName);
  return standardName !== name && existsSync(standard) ? standard : illustration;
}

// Double each illustration pixel with nearest-neighbor sampling before compositing.
export async function renderSocialImage(illustration = fallback, { title = '2dog', summary = '' } = {}) {
  illustration = preferredIllustration(illustration);
  const { width, height } = await sharp(illustration).metadata();
  const scaledWidth = width * 2;
  const scaledHeight = height * 2;
  if (scaledWidth > socialImageWidth - logoInset || scaledHeight > socialImageHeight) {
    throw new Error(`Social illustration ${width}x${height} at 2x (${scaledWidth}x${scaledHeight}) exceeds the 1200x630 canvas with its right inset.`);
  }
  const illustrationImage = await sharp(illustration)
    .resize({ width: scaledWidth, height: scaledHeight, kernel: 'nearest' })
    .png().toBuffer();
  const logoImage = await sharp(logo).resize({ width: logoWidth }).png().toBuffer();
  const copy = await renderCopy(title, summary);
  return sharp(background).composite([
    { input: logoImage, top: logoInset, left: socialImageWidth - logoWidth - logoInset },
    {
      input: illustrationImage,
      top: socialImageHeight - scaledHeight,
      left: socialImageWidth - logoInset - scaledWidth
    },
    ...copy
  ]).png().toBuffer();
}

// Cache by artwork and page copy. Hash the output so changes to the artwork,
// caption, summary or design give social crawlers a new URL.
export function createSocialImageGenerator() {
  const images = new Map();
  return (illustration = fallback, outDir, copy = {}) => {
    illustration = preferredIllustration(illustration);
    const key = JSON.stringify([outDir, illustration, copy.title, copy.summary]);
    if (!images.has(key)) {
      images.set(key, (async () => {
        const image = await renderSocialImage(illustration, copy);
        const hash = createHash('sha256').update(image).digest('hex').slice(0, 12);
        const name = basename(illustration, extname(illustration));
        const path = `social/${name}.${hash}.png`;
        await mkdir(join(outDir, 'social'), { recursive: true });
        await writeFile(join(outDir, path), image);
        return path;
      })());
    }
    return images.get(key);
  };
}
