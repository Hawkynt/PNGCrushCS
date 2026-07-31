# Character generator ROMs

Two fonts are embedded here because a handful of formats cannot be read without them:

| File         | Size | Machine       | Used by       |
| ------------ | ---- | ------------- | ------------- |
| `atari8.fnt.deflate` |  517 | Atari 400/800 | `.sge`, `.dlm` |
| `zx81.fnt.deflate`   |  263 | Sinclair ZX81 | `.zp1`, `.p`  |

These formats store character codes and nothing else — a screen of the machine's built-in font as
the machine held it. The font is not something the file names but something the reader has to
already know, and it cannot be derived: it is a table of shapes somebody drew.

Each is stored as a raw deflate stream of the machine's character generator ROM, dumped verbatim —
a font is half empty space and half repeated edges, so it compresses to about half. The contents are the
original manufacturers' and are reproduced here for interoperability; they are not covered by this
project's licence.
