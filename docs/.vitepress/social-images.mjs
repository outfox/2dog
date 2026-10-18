import { createHash } from 'node:crypto';
import { existsSync } from 'node:fs';
import { mkdir, writeFile } from 'node:fs/promises';
import { basename, extname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

export const socialImageWidth = 1200;
export const socialImageHeight = 630;

const logo = fileURLToPath(new URL('../content/public/logo-static.svg', import.meta.url));
const illustrations = fileURLToPath(new URL('../content/img/', import.meta.url));
const fallback = join(illustrations, '2dog-walkies.png');
const logoWidth = 240;
const logoInset = 48;
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

function preferredIllustration(illustration) {
  if (typeof illustration !== 'string') return illustration;
  // Vite's emitted assets also have a hash: name-big.ABC123.png -> name.png.
  const name = basename(illustration);
  const standardName = name.replace(/-big(?:\.[^.]+)?(?=\.png$)/, '');
  const standard = join(illustrations, standardName);
  return standardName !== name && existsSync(standard) ? standard : illustration;
}

// Only the vector logo is resized. Illustrations are composited pixel for pixel.
export async function renderSocialImage(illustration = fallback) {
  illustration = preferredIllustration(illustration);
  const { width, height } = await sharp(illustration).metadata();
  if (width > socialImageWidth || height > socialImageHeight) {
    throw new Error(`Social illustration ${width}x${height} exceeds the 1200x630 canvas; keep it at its original pixel size.`);
  }
  const logoImage = await sharp(logo).resize({ width: logoWidth }).png().toBuffer();
  return sharp(background).composite([
    { input: logoImage, top: logoInset, left: socialImageWidth - logoWidth - logoInset },
    {
      input: illustration,
      top: socialImageHeight - height,
      left: Math.floor((socialImageWidth - width) / 2)
    }
  ]).png().toBuffer();
}

// Pages sharing an illustration also share its composed image. Hash the output so
// changing the background, logo or illustration gives social crawlers a new URL.
export function createSocialImageGenerator() {
  const images = new Map();
  return (illustration = fallback, outDir) => {
    illustration = preferredIllustration(illustration);
    const key = `${outDir}\0${illustration}`;
    if (!images.has(key)) {
      images.set(key, (async () => {
        const image = await renderSocialImage(illustration);
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
