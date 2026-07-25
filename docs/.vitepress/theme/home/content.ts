/**
 * Entry text — names, details, roles, link labels — may carry inline HTML so an
 * author can place a line break or a non-breaking space where a dock's narrow
 * column needs one: `100%&nbsp;compatible`, `C# on<br />the Web`. Templates
 * render these fields with `v-html` instead of `{{ }}`.
 *
 * Safe here and only here: every one of these strings is a literal in this
 * directory's source, written alongside the markup. Never route user input, URL
 * parameters, or fetched content into a v-html field — that would execute it.
 *
 * Keep the markup inline-level (`<br>`, `<span>`, `<code>`, entities). Block
 * elements break the flex rows these strings sit in.
 */

/** Entry text as a bare string, for `aria-label`s and other plain-text slots. */
export const plain = (html: string) =>
  html
    .replace(/<[^>]*>/g, '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&amp;/g, '&')
    .replace(/\s+/g, ' ')
    .trim()
