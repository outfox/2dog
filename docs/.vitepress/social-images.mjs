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
const accent = '#579eff';
const inset = 64;
const artInset = 48;
const copyWidth = 576;
const logoWidth = 152;
const logoTop = 52;
const titleTop = 168;
const buttonTop = 452;
const buttonHeight = 64;
const fonts = fileURLToPath(new URL('../content/public/fonts/', import.meta.url));
const fontCache = fileURLToPath(new URL('./cache/social-fonts/', import.meta.url));
const fontFiles = new Map();

// The Godot editor's dark blue-grey with a dot grid, lit from behind the artwork.
const background = Buffer.from(`
<svg xmlns="http://www.w3.org/2000/svg" width="${socialImageWidth}" height="${socialImageHeight}">
  <defs>
    <linearGradient id="base" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#05080d"/>
      <stop offset="60%" stop-color="#0c1520"/>
      <stop offset="100%" stop-color="#132538"/>
    </linearGradient>
    <radialGradient id="glow" cx="912" cy="600" r="520" gradientUnits="userSpaceOnUse">
      <stop offset="0%" stop-color="${accent}" stop-opacity="0.34"/>
      <stop offset="45%" stop-color="${accent}" stop-opacity="0.1"/>
      <stop offset="100%" stop-color="${accent}" stop-opacity="0"/>
    </radialGradient>
    <radialGradient id="fade" cx="912" cy="420" r="640" gradientUnits="userSpaceOnUse">
      <stop offset="0%" stop-color="#fff" stop-opacity="1"/>
      <stop offset="100%" stop-color="#fff" stop-opacity="0"/>
    </radialGradient>
    <pattern id="dots" width="24" height="24" patternUnits="userSpaceOnUse">
      <rect x="11" y="11" width="2" height="2" fill="#8fb4e0" fill-opacity="0.16"/>
    </pattern>
    <mask id="grid"><rect width="100%" height="100%" fill="url(#fade)"/></mask>
  </defs>
  <rect width="100%" height="100%" fill="url(#base)"/>
  <rect width="100%" height="100%" fill="url(#dots)" mask="url(#grid)"/>
  <rect width="100%" height="100%" fill="url(#glow)"/>
  <rect width="100%" height="4" fill="${accent}"/>
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

async function renderText(text, size, { bold = false, color = '#f5f8fc', width = copyWidth, spacing = 0, tracking = 0 } = {}) {
  return sharp({ text: {
    text: `<span foreground="${color}" letter_spacing="${tracking * 1024}">${escapeMarkup(text)}</span>`,
    font: `Inter ${bold ? 'Bold ' : ''}${size}`,
    fontfile: await textFont(bold),
    width,
    wrap: 'word-char',
    spacing,
    rgba: true
  }}).png().toBuffer({ resolveWithObject: true });
}

// Shrink the title before the tagline: a long API name needs the room, and the tagline is the invitation.
async function fitCopy(title, tagline) {
  const space = buttonTop - 40 - titleTop;
  const gap = 24;
  // Let dotted API names wrap after a dot, but keep ".NET" whole.
  const titleText = title.replace(/(?<=\w)\.(?=\w)/g, '.\u200b');
  const taglineText = tagline.replace(/\s+/g, ' ').trim();
  // Prefer a slightly smaller title on one line over a larger one broken across two.
  const sizes = [60, 54, 48, 42, 36];
  let first = 0;
  for (const [index, size] of sizes.slice(0, 3).entries()) {
    const line = await renderText(titleText, size, { bold: true, width: socialImageWidth * 4 });
    if (line.info.width <= copyWidth) {
      first = index;
      break;
    }
  }
  for (const titleSize of sizes.slice(first)) {
    const heading = await renderText(titleText, titleSize, { bold: true, spacing: -4 });
    if (heading.info.height > 160) continue;
    for (const taglineSize of [30, 28, 26]) {
      const paragraph = taglineText ? await renderText(taglineText, taglineSize, { color: '#c9d6e6', spacing: 8 }) : undefined;
      if (heading.info.height + gap + (paragraph?.info.height ?? 0) <= space) return { heading, paragraph, gap };
    }
  }
  throw new Error(`Social image copy does not fit; shorten the title or tagline: ${title} / ${tagline}`);
}

async function renderCopy(section, title, tagline) {
  const layers = [];
  const brand = await sharp(logo).resize({ width: logoWidth }).png().toBuffer({ resolveWithObject: true });
  layers.push({ input: brand.data, top: logoTop, left: inset });

  if (section) {
    const label = await renderText(section.toUpperCase(), 17, { bold: true, color: '#9cc5ff', tracking: 2 });
    const pillWidth = label.info.width + 32;
    const pillHeight = 36;
    const pillLeft = inset + logoWidth + 24;
    const pillTop = logoTop + Math.round((brand.info.height - pillHeight) / 2) + 6;
    layers.push(
      { input: Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${pillWidth}" height="${pillHeight}">
        <rect x="0.5" y="0.5" width="${pillWidth - 1}" height="${pillHeight - 1}" rx="${pillHeight / 2}"
          fill="${accent}" fill-opacity="0.14" stroke="${accent}" stroke-opacity="0.55"/>
      </svg>`), top: pillTop, left: pillLeft },
      {
        input: label.data,
        top: pillTop + Math.floor((pillHeight - label.info.height) / 2),
        left: pillLeft + 16
      }
    );
  }

  const { heading, paragraph, gap } = await fitCopy(title, tagline);
  layers.push({ input: heading.data, top: titleTop, left: inset });
  if (paragraph) layers.push({ input: paragraph.data, top: titleTop + heading.info.height + gap, left: inset });

  const label = await renderText('Read the dogs! \u2192', 26, { bold: true, color: '#0b1522' });
  const buttonWidth = label.info.width + 64;
  layers.push(
    { input: Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${buttonWidth}" height="${buttonHeight}">
      <rect x="0.5" y="0.5" width="${buttonWidth - 1}" height="${buttonHeight - 1}" rx="12" fill="${accent}" stroke="#8fc0ff"/>
    </svg>`), top: buttonTop, left: inset },
    {
      input: label.data,
      top: buttonTop + Math.floor((buttonHeight - label.info.height) / 2),
      left: inset + 32
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
export async function renderSocialImage(illustration = fallback, { section = '', title = '2dog', tagline = '' } = {}) {
  illustration = preferredIllustration(illustration);
  let width, height;
  try {
    ({ width, height } = await sharp(illustration).metadata());
  } catch (error) {
    const source = typeof illustration === 'string' ? await readFile(illustration) : illustration;
    if (source.subarray(0, 23).toString() === 'version https://git-lfs') {
      throw new Error(`Social illustration ${illustration} is a Git LFS pointer; fetch it with git lfs pull.`, { cause: error });
    }
    throw error;
  }
  const scaledWidth = width * 2;
  const scaledHeight = height * 2;
  if (scaledWidth > socialImageWidth - inset - copyWidth - artInset || scaledHeight > socialImageHeight) {
    throw new Error(`Social illustration ${width}x${height} at 2x (${scaledWidth}x${scaledHeight}) exceeds the space beside the copy on the 1200x630 canvas.`);
  }
  const illustrationImage = await sharp(illustration)
    .resize({ width: scaledWidth, height: scaledHeight, kernel: 'nearest' })
    .png().toBuffer();
  return sharp(background).composite([
    {
      input: illustrationImage,
      top: socialImageHeight - scaledHeight,
      left: socialImageWidth - artInset - scaledWidth
    },
    ...await renderCopy(section, title, tagline)
  ]).png().toBuffer();
}

// Cache by artwork and page copy. Hash the output so changes to the artwork,
// copy or design give social crawlers a new URL.
export function createSocialImageGenerator() {
  const images = new Map();
  return (illustration = fallback, outDir, copy = {}) => {
    illustration = preferredIllustration(illustration);
    const key = JSON.stringify([outDir, illustration, copy.section, copy.title, copy.tagline]);
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
