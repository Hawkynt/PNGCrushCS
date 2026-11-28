# Sony PictureGear Pocket picture (`.prc`)

The picture format of Sony's CLIÉ handhelds (PEG-S300 onwards): what the handheld's PictureGear
Pocket viewer shows and what Sony's desktop software installs to it. No specification was ever
published. This one was derived from Sony's own code and checked against it:

- `libprcim.dll`, Sony's picture library, in three releases: PictureGear Lite 4.2 on the PEG-S300
  disc (built November 2000), the same program on the PEG-S320 disc (February 2001), and Image
  Converter 1.1 (June 2003, `ImageConverter1.1.zip`). All three are on
  <https://ftp.sonyclie.org/>. It exports `prc_open`, `prc_callback_src`, `prc_read_header`,
  `prc_start_decompress`, `prc_read_rgb_image` and `prc_write_rgb_image`; PictureGear Lite's
  `PgCore.dll` writes through it and Image Converter reads through it.
- the twelve sample pictures on the PEG-S300 and PEG-S320 discs (`PGPocketData`,
  `PGPocket samples`), all 160x160 at eight bits.

Everything below agrees with all three libraries: their decoders give exactly what this layout
says for every sample and for every file their encoders wrote, and they read files built to it.

## Container

A Palm resource database. All numbers are big-endian.

| Offset | Size | Field | Sony writes |
|---|---|---|---|
| 0 | 32 | name, NUL-terminated | the file name without its extension, at most 31 bytes |
| 32 | 2 | attributes | `0x0001` (resource database) |
| 34 | 2 | version | 1 for an eight-bit picture, `0x0110` for sixteen (2003 library) |
| 36 | 4 | created | seconds since 1904-01-01, local time |
| 40 | 4 | modified | the same as created |
| 44 | 4 | last backup | 0 |
| 48 | 4 | modification number | 0 |
| 52 | 4 | app info offset | 0 |
| 56 | 4 | sort info offset | 0 |
| 60 | 4 | type | `IMVS` |
| 64 | 4 | creator | `IMVS` |
| 68 | 4 | unique-id seed | 0 |
| 72 | 4 | next resource list | 0 |
| 76 | 2 | resource count | |
| 78 | 10 each | resource list: type (4), id (2), offset (4) | |

Sony's writer puts the first resource straight after the list, with no gap. A file that has been on
a handheld and come back through HotSync carries the usual Palm traces instead — the name padded
with `0xFF` after its NUL, two bytes between the list and the first resource, a modification number
and a unique-id seed of `0xFFFFFFFF` — and reads the same. A resource runs to the start of the next
one in the file.

Sony's writer emits the resources in this order:

| Type | Id | What |
|---|---|---|
| `iINF` | 1000 | thumbnail info |
| `iFRI` | 1000 | thumbnail frame list |
| `iTIL` | 1000 | the thumbnail, one tile |
| `iINF` | 1100 | picture info |
| `iFRI` | 1100 | picture frame list |
| `iPLT` | 1100 | picture palette (eight-bit pictures only) |
| `iTIL` | 1001, 1002, ... | the picture's tiles |

The readers do not depend on the order. They take the `iINF` numbered 1000 as the thumbnail's and
any other as the picture's (the last one met wins), and also know a `tSTR` resource holding a
NUL-terminated string, which none of the writers produce.

## `iINF` — image info

Nine words, eighteen bytes:

| Word | Field | Thumbnail | Picture |
|---|---|---|---|
| 0 | width | 44 | width |
| 1 | height | 44 | height |
| 2 | — | 0 | 0 |
| 3 | bits per pixel | 8 | 8 or 16 |
| 4 | — | 0 | 0 |
| 5 | tile size | 44 | 32, or 64 when width and height are both at least 320 (2003 library; the earlier ones always use 32) |
| 6 | compression | `0x00FF` | 1, or `0x00FF` when told not to compress |
| 7 | frame list id | 1000 | 1100 |
| 8 | palette id | 0 | 1100 (written even for a sixteen-bit picture, which has no `iPLT`) |

Compression 1 means the tiles are run-length coded; the decoders treat every other value as plain
pixels. PictureGear Lite's `PgCore.dll` sets 1 when its compression setting is on and `0x00FF`
when it is off.

Words 2 and 4 are always nought and nothing reads them.

## `iFRI` — frame list

A count of frames, two words Sony writes as 1 and 0 and nothing reads, then one word per frame: the
id of that frame's first tile. Sony writes one frame, and the decoders use only the first. The
thumbnail's first tile is 1000, the picture's 1001.

## `iPLT` — palette

A word giving the number of entries, then four bytes per entry: nought, red, green, blue.

An eight-bit picture with no palette resource (palette id 0, or an id that is not there) uses the
Palm OS 8-bit system palette; the 2003 decoder does exactly that and the thumbnail always does. Sony's
encoder reduces a picture by median cut to at most 223 colours and then lists the distinct colours
the mapped picture uses, in the order they first occur, stopping at 224; every palette it writes
has at most 224 entries (the samples have 221 to 224).

## `iTIL` — tile

A word giving the length of what follows, then the tile: tile-size rows of tile-size pixels,
left to right, top to bottom, either plain or run-length coded. A picture is tiled left to right,
then top to bottom, from the frame's first tile id upwards; the last column and row of tiles run
past the picture, and Sony fills the part outside it with index 0 at eight bits and with `0xFFFF`
(white) at sixteen.

Pixels are one byte (a palette index) at eight bits and one big-endian word of RGB 5:6:5 at
sixteen — red in the top five bits. Sony's decoder widens each field to eight bits as
`value * 255 / 31` (and `/ 63` for green), truncating.

### Run-length coding

Elements are bytes at eight bits and words at sixteen. Decoding: copy an element; if the next
element is equal to it, copy that too and read one count byte, then copy the element that many
more times. Encoding mirrors it: a run of *n* ≥ 2 equal elements is written as two copies and the
count *n* − 2, which is at most 255, so a longer run is split and the next piece starts afresh.
A lone element is written as itself. The coding is not escaped and can be longer than the tile
(Sony stores it anyway; tile 1005 of `sample Sky.prc` is 1086 bytes for 1024 pixels).

The decoders stop at the end of the tile and at the end of the data; a short stream leaves the
rest of the tile as it was.

## Thumbnail

44x44, eight bits, plain, against the Palm OS 8-bit system palette, of which Sony's encoder uses
the first 231 entries (215 colours of the six-level cube in Palm order without black, ten greys,
silver, maroon, purple, green, teal, black). Sony's encoders average the picture over a 44x44 grid
— the 2000/2001 library stretches it, the 2003 one keeps the aspect ratio and centres it on black —
and dither it onto those colours with Floyd–Steinberg weights.

## What is not supported

No Sony software reads or writes any depth but 8 and 16; the decoders take every depth below 16 as
one byte per pixel. XnView's converter writes `.prc` at four bits a pixel, two pixels a byte, with
its frame list's second word 0, a blank thumbnail, an empty name, a tile whose stated length is not
its size, and the file cut off before its last tile; Sony's 2003 decoder refuses such a file and
so does this reader.
