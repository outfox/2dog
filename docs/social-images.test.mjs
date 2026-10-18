import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';
import { createSocialImageGenerator, renderSocialImage } from './.vitepress/social-images.mjs';

test('composes a 1200x630 gradient with the logo and an unscaled bottom-center illustration', async () => {
  const illustration = await sharp({
    create: { width: 31, height: 17, channels: 4, background: '#f02878' }
  }).png().toBuffer();
  const image = await renderSocialImage(illustration);
  const metadata = await sharp(image).metadata();
  assert.equal(metadata.width, 1200);
  assert.equal(metadata.height, 630);
  const { data, info } = await sharp(image).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const pixel = (x, y) => [...data.subarray((y * info.width + x) * 4, (y * info.width + x) * 4 + 4)];
  const left = Math.floor((1200 - 31) / 2);
  const top = 630 - 17;
  const marker = [240, 40, 120, 255];
  assert.deepEqual(pixel(left, top), marker);
  assert.deepEqual(pixel(left + 30, 629), marker);
  assert.notDeepEqual(pixel(left - 1, top), marker);
  assert.notDeepEqual(pixel(left + 31, top), marker);
  assert.notDeepEqual(pixel(left, top - 1), marker);
  assert.deepEqual(pixel(0, 0), [0, 0, 0, 255]);
  const [red, green, blue, alpha] = pixel(1199, 629);
  assert.ok(red < green && green < blue && blue < 70, 'faint steel blue at the end of the gradient');
  assert.equal(alpha, 255);
  const logo = await sharp(image).extract({ left: 912, top: 48, width: 240, height: 124 }).stats();
  assert.ok(logo.channels[0].max > 240, 'white logo appears in the top-right corner');
});

test('uses the standard illustration instead of a -big asset, including hashed Vite filenames', async () => {
  const standard = fileURLToPath(new URL('./content/img/2dog-walkies.png', import.meta.url));
  const expected = await renderSocialImage(standard);
  for (const name of ['2dog-walkies-big.png', '2dog-walkies-big.ABC123.png']) {
    assert.deepEqual(await renderSocialImage(join('emitted/assets', name)), expected);
  }
});

test('shares hashed images for repeated artwork and writes fetchable PNGs', async (t) => {
  const dir = await mkdtemp(join(tmpdir(), '2dog-social-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  const generate = createSocialImageGenerator();
  const [first, second] = await Promise.all([generate(undefined, dir), generate(undefined, dir)]);
  assert.equal(first, second);
  assert.match(first, /^social\/2dog-walkies\.[a-f0-9]{12}\.png$/);
  assert.equal((await readdir(join(dir, 'social'))).length, 1);
  const image = await readFile(join(dir, first));
  assert.equal((await sharp(image).metadata()).width, 1200);
  assert.equal((await sharp(image).metadata()).height, 630);
});

test('rejects oversized illustrations instead of resizing or cropping them', async () => {
  const illustration = await sharp({
    create: { width: 1201, height: 10, channels: 4, background: '#fff' }
  }).png().toBuffer();
  await assert.rejects(renderSocialImage(illustration), /exceeds the 1200x630 canvas/);
});
