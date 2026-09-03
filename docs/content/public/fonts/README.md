# Fonts

Vendored from `godot/thirdparty/fonts/` (the same faces the Godot editor ships)
and **subset** for the web: 311 KB → 122 KB across the three files.

The ranges cover every non-ASCII character used anywhere in `docs/` – Latin-1,
Latin Extended-A, general punctuation, arrows, math operators, geometric
shapes, box drawing (the `├── └──` project trees), and currency. The one
character outside them is 🦴, which comes from the system emoji font in every
case.

Regenerate after replacing a font, or after introducing a character outside
these ranges:

```bash
R="U+0000-00FF,U+0100-017F,U+2000-206F,U+2190-21FF,U+2200-22FF,U+25A0-25FF,U+2500-257F,U+20A0-20BF"
uv run --with fonttools --with brotli pyftsubset Inter_Regular.woff2 \
  --unicodes="$R" --layout-features='*' --flavor=woff2 \
  --output-file=Inter_Regular.subset.woff2
```

Both faces are SIL Open Font License 1.1 with no Reserved Font Name, so subsets
redistribute under the same license without renaming. `LICENSE.Inter.txt` and
`LICENSE.JetBrainsMono.txt` travel with them.
