// CI builders (statichost.eu) clone shallow, which collapses every page's git lastmod
// to the HEAD commit date. Fetch full history so the sitemap gets real per-page dates.
import { execSync } from 'node:child_process'

const git = (args) => execSync(`git ${args}`, { encoding: 'utf8' }).trim()

try {
  if (git('rev-parse --is-shallow-repository') === 'true') {
    console.log('unshallow: shallow clone detected, fetching full history...')
    git('fetch --unshallow --quiet')
    console.log('unshallow: done')
  } else {
    console.log('unshallow: repository already has full history')
  }
} catch (err) {
  console.warn(`unshallow: skipped (${err.message.split('\n')[0]}); sitemap lastmod may be inaccurate`)
}
