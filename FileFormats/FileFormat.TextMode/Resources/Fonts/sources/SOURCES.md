# Embedded font source provenance

Each `.bin` file in this directory is a raw glyph dump fed into `Tools/FontBaker` which deflate-compresses it into the corresponding `../{family}.fnt.dfl` embedded resource consumed by `BitmapFontEmbedded`. When a `.bin` is absent for a given family the baker falls back to `EraFontGenerator`'s procedural placeholders.

## Provenance and licensing

The bitmap data below is widely treated as de-facto public domain through 40+ years of unrestricted redistribution in emulators and BIOS replacements (QEMU, DOSBox, Bochs, VICE, Altirra, Linux fbcon). The original vendors (IBM, Commodore, Atari) are either defunct, never registered copyright on the bitmap data, or have publicly tolerated wholesale embedding for decades.

| File | Bytes | Origin | Source URL |
|---|---|---|---|
| `ibm-vga-8x16.bin` | 4096 | IBM VGA BIOS character ROM | `https://github.com/spacerace/romfont/blob/master/font-bin/IBM_VGA_8x16.bin` |
| `ibm-vga-8x14.bin` | 3584 | IBM VGA BIOS character ROM (EGA-compat) | `https://github.com/spacerace/romfont/blob/master/font-bin/IBM_VGA_8x14.bin` |
| `ibm-vga-8x8.bin` | 2048 | IBM VGA BIOS character ROM (CGA-compat) | `https://github.com/spacerace/romfont/blob/master/font-bin/IBM_VGA_8x8.bin` |
| `c64-petscii-8x8.bin` | 4096 | Commodore 64 character generator ROM `901225-01` (bank 0 = uppercase + graphics; bank 1 = lowercase + uppercase) | `https://www.zimmers.net/anonftp/pub/cbm/firmware/computers/c64/characters.901225-01.bin` |
| `atari-atascii-8x8.bin` | 1024 | Altirra Replacement OS character set (Avery Lee, BSD-3-Clause) extracted from `atari800/src/roms/altirra_5200_charset.c` | `https://github.com/atari800/atari800/blob/master/src/roms/altirra_5200_charset.c` |

## Amiga Topaz 8x16

No authentic ROM dump was located via the search paths exercised during the initial port. The Topaz slot in `BitmapFontEmbedded` currently falls back to a procedural variant (VGA with horizontal double-strike). To upgrade: drop a real Topaz 8x16 binary at `amiga-topaz-8x16.bin` (4096 bytes, MSB-leftmost row layout matching the existing files) and re-run `dotnet run --project Tools/FontBaker`.

## Re-baking

```
dotnet run --project Tools/FontBaker
```

The baker overwrites the `*.fnt.dfl` files in the parent directory; commit them alongside any `.bin` source changes.
