# Documentation site

`docs/` builds 2dog.dev with VitePress. These rules add to the root `AGENTS.md`; run commands from `docs/`.

## Layout

- Pages are Markdown in `content/`, the VitePress `srcDir`. Guides sit at the top level; `hosts/`,
  `configuration/`, `cli/`, `api/` and `known-issues/` hold references.
- `.vitepress/config.mts` holds the nav, sidebar and site metadata, `.vitepress/plugins/` the Markdown
  plugins and `.vitepress/theme/` styling and layout. The homepage is `content/index.md` frontmatter rendered
  by `theme/EditorHome.vue` and `theme/home/`, so its text lives in those components.
- `npm run dev` and `npm run build` regenerate the ignored `content/public/llms.txt` and `llms-full.txt`;
  postbuild mirrors the Markdown into `.vitepress/dist/`. Edit sources, never generated output.
- statichost.eu builds and hosts 2dog.dev from pushes with `npm run build-ci`; there is no manual deploy.

## Pages

- Give every page frontmatter `title` and a quoted one-sentence `description`. The description becomes the
  meta description and the page's `llms.txt` entry.
- List a new page in the sidebar in `.vitepress/config.mts` and in the matching section of `SECTIONS` in
  `make-llms.mjs`. Pages that must stay out of the index go in `EXCLUDE`.
- When a page moves or merges, keep a redirect stub at the old path like `content/web.md`: `search: false`,
  a meta refresh, no title or description, listed in `EXCLUDE`. Point internal links at the new page.
- Each host has `hosts/<host>.md` and `configuration/<host>.md`. Sidebar host lists start with the generic
  host, then run alphabetically.
- Each visible CLI verb has `cli/<verb>.md`, linked from the sidebar as `/cli/<verb>`. `dnx-2dog.md` and the
  verb pages mention every visible option, and `cli/doctor.md` lists every check id in backticks. Keep
  `twodog/README.md` in step; `DocsDriftTests` checks all of this.

## Markdown

- Never hard-code versions: `:2dog-version:`, `:godot-version:` and `:natives-version:` resolve from
  `Directory.Build.props`, also inside code.
- `:gd-<name>:` and `:gd-<name>@<tint>:` render icons from `content/public/icons/`; unknown names or tints
  fail the build. Cards use `:::: columns` around `::: column <title>` blocks; the outer fence needs four
  colons. Callouts use `::: tip`, `::: info` and `::: warning`.
- Link pages without `.md`, site-absolute (`/hosts/web`) or relative (`./web#properties`). Link repository
  files through `https://github.com/outfox/2dog/tree/main/`.
- Examples call the game `MyGame` and its hosts `MyGame.<host>`.
- Wrap prose near 80 columns. Use tables for options and properties, `bash` fences for commands and `text`
  fences for output.
- Avoid em-dashes, curly quotes and the ellipsis character. `node fix-typography.mjs <file>` replaces them;
  the local pre-commit hook runs it on staged Markdown, but not every checkout has that hook.

## Verify

```text
npm ci
npm test         # Page index validation tests
npm run check    # Every page has metadata and one index entry
npm run build    # Also fails on dead internal links and unknown icons
dotnet test ../twodog.tests --filter "FullyQualifiedName~DocsDriftTests"   # After CLI page changes
```
