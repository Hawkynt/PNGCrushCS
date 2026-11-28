# LuraDocument (`.ldf`) — derived specification

LuraTech never published the LuraDocument format. This document is written from observation of LuraTech's own
encoder and decoder. Everything stated here was checked against that decoder's output. The general idea is
mixed raster content (MRC): a bilevel mask chooses, pixel by pixel, between a foreground and a background that are
both stored at reduced resolution. Barthel, McPartlin and Thierschmann describe this in "New technology for raster
document image compression" (SPIE 3967, 2000), and LuraTech's patent US 6,731,800 B1 covers it. Neither gives the
bitstream.

## Provenance

* **Reference codec.** IrfanView's LuraDocument plug-in, `LDF.DLL` 3.3.7.0 (2012-07-31, 228,864 bytes, SHA-256
  `bc4ab1114154b5336f40c5a0afcd99c09ea018e12e18943e8c8a42d2e5ebd255`). It is built on LuraTech's SDK and statically
  links the whole codec, including its `ILD_Compress_*` and `ILD_Decompress_*` entry points. It is packed with ASPack,
  so it was loaded under Wine, dumped from memory and decompiled with Ghidra wherever behaviour had to be explained.
  A small harness drove its exports `SaveLDF(dib, file, mode, quality, thumbnail)`,
  `AddLDF_Page(file, dib, mode, quality, thumbnail, position)` and `ReadLDF(file, size, page, error, …)`. To see a
  single layer, the harness also patched the three layer flags that `ReadLDF` passes to `ILD_Decompress_Merge`. This
  is the oracle.
* **Rung.** This is rung 2 of the sourcing ladder. The plug-in is proprietary. It was decompiled and run to write this
  document, and none of its code was carried over. The colour weights and the mask cipher's constants are constants,
  and they are copied exactly. The mask is JBIG, which is implemented from ITU-T T.82 itself (rung 3). T.82's
  probability table is copied from the standard, and the plug-in carries the identical table. jbig-kit 2.2 (GPL)
  served only as a second oracle for the JBIG decoder.
* **Files.** The plug-in's encoder wrote every file the reader was checked on. They cover colour, grey,
  black-and-white and palette pictures, sizes from 37×23 to 3000×2200, all five of its modes, qualities 1 to 100,
  with and without thumbnails, and documents of up to four pages. All 116 of those pages decode exactly as the plug-in
  decodes them. So do 8 pages modified by hand (layers unlinked, resolutions changed with the mask re-keyed) and 23
  pages from this package's writer. No LuraDocument file from any other program was found.

## Container

A LuraDocument file is little-endian TIFF (`II*\0`). The plug-in reads its bytes by hand and knows no other byte
order. Its directory chain holds, for each page, a **page directory** followed by that page's **layer directories**.
Each directory names itself with private tag 37637 (`0x9305`), which is ASCII with a count of 4:

| Name | Directory | Compression (259) | Contents |
| --- | --- | --- | --- |
| `bit` | page | 34690, or absent | Page size, resolution, page number. When compression is 34690, its strip is the scrambled JBIG mask (1 bit, photometric 1). |
| `thm` | layer | 34692 | Thumbnail, LuraWave. Optional. |
| `for` | layer | 34692 | Foreground, LuraWave, 1 or 3 samples (photometric 1 or 2). |
| `bac` | layer | 34692 | Background, LuraWave, 1 or 3 samples. |
| `txt` | layer | — | The plug-in knows the name but never writes it. |

The page directory also carries: 269 and 270 (empty), 274 = 1, 282 and 283 (resolution, rational), 297
(PageNumber: index and count), 305 (four bytes `01 01 00 00` with type BYTE), and private tags 33723 (IPTC) and
37638 (annotations). The last two are empty in every file the plug-in writes. A page without a mask has no
compression, bits-per-sample, photometric or samples-per-pixel tag, and its strip is empty.

The layout matters to the plug-in. Each page's directories are written back to back, followed by their out-of-line
values, and all strips come at the end. To find a page's layer named X, the plug-in starts at the page directory and
walks on while the next directory begins exactly where the previous one ends. It does not stop at the next `bit`
directory. If a page's directories are not followed by a gap, it takes the next page's layers. This reader instead
groups directories by name: everything from one `bit` directory up to the next belongs to that page. The two
readings agree on every file laid out the way LuraTech lays them out, and this package's writer lays files out that
way.

Compression 34693 is what the plug-in's encoder switches to for a LuraWave layer of a megabyte or more. It never
did so for any page up to 3000×2200, and the reader accepts it as LuraWave. The plug-in also reads a plain TIFF page
in CCITT Group 4 (compression 4, no 37637 tag) as a mask at 75 dpi. That is a fax import, not LuraDocument, and
it is refused here by name.

## Mask

The strip of a `bit` directory with compression 34690 is a JBIG bi-level image entity (ITU-T T.82), XOR-scrambled
byte by byte.

**Scrambling.** Let `r` be the larger of the two whole-number resolutions (numerator divided by denominator,
integer division) and `w × h` the page size. A 32-bit state starts at `s = r + w + 13 + h`. For each byte, the state
steps first, and then the byte is XORed with the state's low eight bits:

```
s ^= ((~s >> 1) + 1) ^ (s << 1)
byte ^= s & 0xFF
```

The same operation unscrambles. Changing the resolution tags changes the key, and the plug-in then decodes noise.
With a resolution of zero it refuses the page.

**JBIG.** LuraTech's encoder is evidently jbig-kit. Its default stripe height (a 35th of the image, 2 to 128 lines),
its option byte `0x1C`, order `3` and `MX = 8` all match jbig-kit's defaults, and so do its deterministic-prediction
table conversion routines. Masks always have a single resolution layer (`DL = D = 0`) and one plane. The reader
implements all of T.82 for that layer: both templates, typical prediction, `ATMOVE`, `NEWLEN`, `COMMENT` and
`SDRST`. It refuses differential layers by name. A stripe whose coded data is empty is just `FF 02`.

**Polarity.** The mask is TIFF "black is zero". A **clear** bit selects the foreground, and a set bit selects the
background.

## Layers

Each layer strip is a complete LuraWave stream (`WV 02 00`, see `../LuraWave/SPEC.md`) in colour mode 1 (grey) or
4 (three plain channels, each `c + 128`). LuraTech's encoder always writes 7 wavelet levels and a coded grid of at
least 256 on each side, rounded up to a multiple of 128. Large layers are written as 128-row stripes without an
`ST` chunk. It also adds `DP` (1200 dpi) and `MV` (255) and leaves out `QU`. The plug-in also decodes the leaner
streams this package's LuraWave writer makes.

A three-channel layer holds luma `Y`, then a blue difference `U` and a red difference `V`, both centred on 127.
The **background stores `255 − Y`**, and the foreground stores `Y` as it is. A grey layer holds the luma alone, with
the same inversion for the background. The decoder turns a three-channel layer into RGB at layer resolution, with
`÷1024` truncating toward zero and each result clamped to 0…255:

```
R = (1024·Y + 1163·(V−127)) / 1024
G = (1024·Y −  406·(U−127) − 592·(V−127)) / 1024
B = (1024·Y + 2090·(U−127)) / 1024
```

The encoder's forward transform is `Y = (601·G + 306·R + 117·B) >> 10`,
`U = ((B−Y)·504 + 127·1024) >> 10`, and `V = clamp((R−Y)·898 / 1024 + 127)`. This writer does not need it.

## Composing a page

1. **Enlargement.** Each layer is enlarged by one whole-number factor `f`. If the layer is narrower than it is tall,
   `f = ⌈page height / layer height⌉`. Otherwise `f = ⌈page width / layer width⌉`. Layer pixel `(i, j)` lands on page
   pixel `(f·i, f·j)`. Each page pixel `(x, y)` interpolates first along the row and then down the column, between
   layer pixels `x0 = min(⌊x/f⌋, w−1)` and `x1 = min(x0+1, w−1)` (likewise in `y`):
   `a + (b − a)·(x mod f) / f`, truncated toward zero at each of the two steps. LuraTech's encoder always reduces the
   foreground and background by 3, and sizes them `⌈w/3⌉ × ⌈h/3⌉`.
2. **What the page is.** A page with no foreground and no background is bilevel: set mask bits are white and clear
   bits are black. If any layer has three channels, the page is colour. Otherwise it is grey, and a grey layer on a
   colour page is used as equal R, G and B.
3. **Selection.** Without a mask, the page is the background. With a mask, clear bits take the foreground and set
   bits take the background. A missing background is white.
4. **A mask without a foreground.** The plug-in then ignores the background and returns the mask alone as a
   bilevel page. This was found by unlinking the `for` directory from a file the plug-in wrote. Its own encoder
   never writes such a page.

Thumbnails are not used for a full decode.

## The plug-in's encoder

`SaveLDF`'s mode picks one of LuraTech's internal compression profiles. Modes 0, 1 and 2 (internally 201, 202 and
200) are three MRC profiles, and each gives a mask, a foreground and a background. Mode 3 (internally 203) is
"photo": no mask, and one full-size background. Mode 4 keeps the mask alone. Grey and black-and-white pictures get
grey layers. The quality argument sets the layer budgets: 1 and 10 gave identical files, and so did 90 and 100. The
thumbnail flag adds a `thm` layer. The
plug-in takes the picture's horizontal pixels-per-metre field as dots per inch and caps it at 1200. The
segmentation into mask, foreground and background is the encoder's own business; a decoder does not need it.

## Writing

This writer does not segment. It writes each page in one of two layouts the plug-in itself uses:

* a black-and-white picture as a mask alone (mode 4's layout), which is lossless;
* grey or colour as one full-size background with no mask (mode 3's layout). Grey is lossless. For colour, each
  pixel is stored as whichever (Y, U, V) the decoder turns into the nearest RGB. The search starts at the forward
  transform and takes single steps in Y, U and V while they reduce the squared error. Most colours come back within
  1. Saturated colours outside the layer's gamut come back as close as it allows.

The mask is coded with the three-line template, typical prediction, no adaptive-pixel moves and LuraTech's stripe
height. Resolution defaults to 300 dpi. The plug-in composes every committed writer fixture exactly as this reader
does.

## Unknown

* Whether any other program writes LuraDocument, and whether LuraTech's own LuraDocument products put anything in
  the IPTC (33723) and annotation (37638) tags, or a `txt` layer, that changes how a page is composed.
* What compression 34693 changes beyond its number.
