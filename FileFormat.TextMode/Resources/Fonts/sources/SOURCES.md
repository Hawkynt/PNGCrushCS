# Embedded font source provenance

Each `.bin` file in this directory is a raw glyph dump fed into `Tools/FontBaker`, which deflate-compresses it into a `../*.fnt.dfl` embedded resource consumed by `BitmapFontEmbedded`. When a `.bin` is absent for a family the baker falls back to `EraFontGenerator`'s procedural placeholders.

The mapping is not by name and is not one-to-one, which is worth stating because the file names invite the opposite assumption. `Tools/FontBaker/Program.cs` holds the table; it is reproduced in the last column below. One dump feeds two families — a C64 character generator ROM is two 2048-byte banks, and the baker slices the requested one — and the two IBM dumps are named for the adapter whose ROM they came off rather than for the family they end up as.

## Provenance and licensing

The bitmap data below is widely treated as de-facto public domain through 40+ years of unrestricted redistribution in emulators and BIOS replacements (QEMU, DOSBox, Bochs, VICE, Altirra, Linux fbcon). The original vendors (IBM, Commodore, Atari) are either defunct, never registered copyright on the bitmap data, or have publicly tolerated wholesale embedding for decades.

| File | Bytes | Bakes into | Origin | Source URL |
|---|---|---|---|---|
| `ibm-vga-8x16.bin` | 4096 | `ibm-vga-8x16.fnt.dfl` | IBM VGA BIOS character ROM | `https://github.com/spacerace/romfont/blob/master/font-bin/IBM_VGA_8x16.bin` |
| `ibm-vga-8x14.bin` | 3584 | `ibm-ega-8x14.fnt.dfl` | IBM VGA BIOS character ROM (EGA-compat) | `https://github.com/spacerace/romfont/blob/master/font-bin/IBM_VGA_8x14.bin` |
| `ibm-vga-8x8.bin` | 2048 | `ibm-cga-8x8.fnt.dfl` | IBM VGA BIOS character ROM (CGA-compat) | `https://github.com/spacerace/romfont/blob/master/font-bin/IBM_VGA_8x8.bin` |
| `c64-petscii-8x8.bin` | 4096 | `c64-petscii-8x8.fnt.dfl` (bank 0) and `c64-petscii-lo-8x8.fnt.dfl` (bank 1) | Commodore 64 character generator ROM `901225-01` (bank 0 = uppercase + graphics; bank 1 = lowercase + uppercase) | `https://www.zimmers.net/anonftp/pub/cbm/firmware/computers/c64/characters.901225-01.bin` |
| `atari-atascii-8x8.bin` | 1024 | `atari-atascii-8x8.fnt.dfl` | Altirra Replacement OS character set (Avery Lee, BSD-3-Clause) extracted from `atari800/src/roms/altirra_5200_charset.c` | `https://github.com/atari800/atari800/blob/master/src/roms/altirra_5200_charset.c` |

The ATASCII dump is 1024 bytes — 128 glyphs of 8 rows — where the embedded loader wants a 256-glyph buffer, so the baker mirrors it into the upper half. That is the format's own inverse-video convention and not padding.

## Amiga Topaz 8x16

No authentic ROM dump was located via the search paths exercised during the initial port. The Topaz slot in `BitmapFontEmbedded` currently falls back to a procedural variant (VGA with horizontal double-strike). To upgrade: drop a real Topaz 8x16 binary at `amiga-topaz-8x16.bin` (4096 bytes, MSB-leftmost row layout matching the existing files) and re-run `dotnet run --project Tools/FontBaker`.

## Re-baking

```
dotnet run --project Tools/FontBaker -- FileFormat.TextMode/Resources/Fonts
```

**Pass the path.** The baker's default output directory is computed five levels up from its own
build output and then down through a `FileFormats/` folder that does not exist in this repository, so
running it bare creates that folder and writes seven `.fnt.dfl` files nobody reads, leaving the real
resources untouched and the run looking successful. The argument above is the directory this file
sits under.

The baker overwrites the `*.fnt.dfl` files in the parent directory; commit them alongside any `.bin` source changes.
