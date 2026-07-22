// Two-column card layout for the docs.
//
// Usage in markdown — the OUTER container needs the extra colon, because a
// closing fence only requires at least as many colons as its opener:
//
//   :::: columns
//   ::: column 🐕 Card Title
//   Any markdown, including fenced code blocks.
//   :::
//   ::: column 🌱 Another Card
//   ...
//   :::
//   ::::
//
// Cards sit side by side and collapse to a single column on narrow
// screens. Styling lives in theme/custom.css (.md-columns / .md-column).

import container from 'markdown-it-container'
import type MarkdownIt from 'markdown-it'

export function columnsPlugin(md: MarkdownIt) {
  md.use(container, 'columns', {
    render: (tokens, idx) =>
      tokens[idx].nesting === 1 ? '<div class="md-columns">\n' : '</div>\n'
  })

  md.use(container, 'column', {
    render: (tokens, idx) => {
      const token = tokens[idx]
      if (token.nesting === -1) return '</div>\n'
      const title = token.info.trim().replace(/^column\s*/, '')
      const heading = title
        ? `<p class="md-column-title">${md.renderInline(title)}</p>\n`
        : ''
      return `<div class="md-column">\n${heading}`
    }
  })
}
