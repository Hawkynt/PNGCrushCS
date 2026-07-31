# Character generator ROMs

Two fonts are embedded here because a handful of formats cannot be read without them:

| File         | Size | Machine       | Used by       |
| ------------ | ---- | ------------- | ------------- |
| `atari8.fnt` | 1024 | Atari 400/800 | `.sge`, `.dlm` |
| `zx81.fnt`   |  512 | Sinclair ZX81 | `.zp1`, `.p`  |

These formats store character codes and nothing else — a screen of the machine's built-in font as
the machine held it. The font is not something the file names but something the reader has to
already know, and it cannot be derived: it is a table of shapes somebody drew.

Each is the character generator ROM of the machine named, dumped verbatim. The contents are the
original manufacturers' and are reproduced here for interoperability; they are not covered by this
project's licence.
