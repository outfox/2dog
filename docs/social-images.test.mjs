import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';
import { createSocialImageGenerator, renderSocialImage } from './.vitepress/social-images.mjs';

test('composes a 1200x630 gradient with the logo and a 2x nearest-neighbor bottom-right illustration', async () => {
  const colors = [[240, 40, 120, 255], [40, 120, 240, 255]];
  const source = Buffer.alloc(31 * 17 * 4);
  for (let y = 0; y < 17; y++) {
    for (let x = 0; x < 31; x++) source.set(colors[(x + y) % 2], (y * 31 + x) * 4);
  }
  const illustration = await sharp(source, { raw: { width: 31, height: 17, channels: 4 } }).png().toBuffer();
  const image = await renderSocialImage(illustration);
  const metadata = await sharp(image).metadata();
  assert.equal(metadata.width, 1200);
  assert.equal(metadata.height, 630);
  const { data, info } = await sharp(image).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const pixel = (x, y) => [...data.subarray((y * info.width + x) * 4, (y * info.width + x) * 4 + 4)];
  const left = 1200 - 48 - 62;
  const top = 630 - 34;
  const marker = colors[0];
  assert.deepEqual(pixel(left, top), marker);
  assert.deepEqual(pixel(left + 61, 629), marker);
  assert.notDeepEqual(pixel(left - 1, top), marker);
  assert.notDeepEqual(pixel(left + 62, top), marker);
  assert.notDeepEqual(pixel(left, top - 1), marker);
  for (let y = 0; y < 17; y++) {
    for (let x = 0; x < 31; x++) {
      for (const [dx, dy] of [[0, 0], [1, 0], [0, 1], [1, 1]]) {
        assert.deepEqual(pixel(left + x * 2 + dx, top + y * 2 + dy), colors[(x + y) % 2]);
      }
    }
  }
  assert.deepEqual(pixel(0, 0), [0, 0, 0, 255]);
  const [red, green, blue, alpha] = pixel(1199, 629);
  assert.ok(red < green && green < blue && blue < 70, 'faint steel blue at the end of the gradient');
  assert.equal(alpha, 255);
  const logo = await sharp(image).extract({ left: 912, top: 48, width: 240, height: 124 }).stats();
  assert.ok(logo.channels[0].max > 240, 'white logo appears in the top-right corner');
  assert.deepEqual(pixel(64, 468), [87, 158, 255, 255], 'CTA button leaves clearance above social overlays');
});

test('uses the standard illustration instead of a -big asset, including hashed Vite filenames', async () => {
  const standard = fileURLToPath(new URL('./content/img/2dog-walkies.png', import.meta.url));
  const expected = await renderSocialImage(standard);
  for (const name of ['2dog-walkies-big.png', '2dog-walkies-big.ABC123.png']) {
    assert.deepEqual(await renderSocialImage(join('emitted/assets', name)), expected);
  }
});

test('shares hashed images only for identical artwork and copy, and writes fetchable PNGs', async (t) => {
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
  const differentTitle = await generate(undefined, dir, { title: 'Web', summary: 'Run Godot C# in a browser.' });
  const differentSummary = await generate(undefined, dir, { title: 'Web', summary: 'Publish a browser game.' });
  assert.notEqual(first, differentTitle);
  assert.notEqual(differentTitle, differentSummary);
});

test('fits long API captions and summaries while escaping markup in page text', async () => {
  const image = await renderSocialImage(undefined, {
    title: 'twodog.Testing.Xunit.RenderingCollection <T> & Tests',
    summary: 'Run <Godot> & .NET tests safely. '.repeat(20)
  });
  const heading = await sharp(image).extract({ left: 48, top: 56, width: 576, height: 168 }).stats();
  assert.ok(heading.channels[0].max > 240, 'title is rendered in the reserved heading area');
});

test('uses the proportional Inter font instead of silently falling back to a system font', async () => {
  const headingWidth = async (title) => {
    const image = await renderSocialImage(undefined, { title });
    const { data, info } = await sharp(image).extract({ left: 48, top: 56, width: 576, height: 168 })
      .removeAlpha().raw().toBuffer({ resolveWithObject: true });
    let right = 0;
    for (let y = 0; y < info.height; y++) {
      for (let x = 0; x < info.width; x++) {
        const offset = (y * info.width + x) * info.channels;
        if (data[offset] > 240 && data[offset + 1] > 240 && data[offset + 2] > 240) right = Math.max(right, x);
      }
    }
    return right + 1;
  };
  assert.ok(await headingWidth('WWWW') > (await headingWidth('iiii')) * 2);
});

test('rejects illustrations that would overflow the canvas at 2x instead of cropping them', async () => {
  const illustration = await sharp({
    create: { width: 601, height: 10, channels: 4, background: '#fff' }
  }).png().toBuffer();
  await assert.rejects(renderSocialImage(illustration), /exceeds the 1200x630 canvas/);
});
