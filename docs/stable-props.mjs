// The docs describe the stable release, the one plain `dnx 2dog` installs. main's tree may hold a Godot prerelease
// line (4.8 beta), and other lines are only tagged, so versions come from the newest release tag without a
// prerelease label. Offline, the working tree's props stand in, with a warning.
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
// Anonymous HTTPS: the docs builder's SSH clone has no GitHub host key (see unshallow.mjs).
const repo = 'https://github.com/outfox/2dog.git'
const git = (...args) =>
  execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim()

export function stableProps() {
  try {
    const [sha, tag] = git('ls-remote', '--tags', '--refs', repo, 'refs/tags/v*')
      .split('\n')
      .map((line) => line.split('\t'))
      .filter(([, ref]) => /^refs\/tags\/v\d+(\.\d+){3}$/.test(ref))
      .map(([sha, ref]) => [sha, ref.slice('refs/tags/'.length)])
      .sort(([, a], [, b]) => compareVersions(b.slice(1), a.slice(1)))[0]
    try {
      git('cat-file', '-e', `${sha}^{commit}`)
    } catch {
      git('fetch', '--quiet', '--no-tags', repo, `refs/tags/${tag}`)
    }
    let props = git('show', `${sha}^{commit}:Directory.Build.props`)
    // Older releases store their versions in the tagged commit.
    if (git('cat-file', '-t', sha) === 'tag') {
      const signed = git('cat-file', '-p', sha).match(/^2dog-release-v1 [0-9a-f]{40} (\d+)\r?$/m)
      if (signed) {
        const [, godot, revision] = /^v(\d+\.\d+\.\d+)\.(\d+)$/.exec(tag)
        const values = { GodotVersion: godot, TwoDogRevision: revision, NativesRevision: signed[1], TwoDogPrerelease: '' }
        for (const [name, value] of Object.entries(values)) {
          props = props.replace(new RegExp(`<${name}>[^<]*</${name}>`), `<${name}>${value}</${name}>`)
        }
      }
    }
    return props
  } catch (err) {
    console.warn(`stable-props: no stable release tag (${err.message.split('\n')[0]}); using the working tree's versions`)
    return readFileSync(join(root, 'Directory.Build.props'), 'utf8')
  }
}

function compareVersions(a, b) {
  const x = a.split('.').map(Number)
  const y = b.split('.').map(Number)
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    if ((x[i] ?? 0) !== (y[i] ?? 0)) return (x[i] ?? 0) - (y[i] ?? 0)
  }
  return 0
}
