# LuraWave (`.lwf`) — derived specification

LuraTech never published the LuraWave bitstream. This document is a description written from
observation of LuraTech's own decoder. Every statement here was checked against that decoder's output.

## Provenance

* **Material.** `com.luratech.lwf.lwfDecoder` in `lwf_jsdk2.6.jar` (one class, dated 2002-04-24),
  redistributed under licence by McMaster University in the MBF ImageJ bundle
  (`https://imagej.net/ij/plugins/mbf/mbf-plugins.zip`). The decoder refuses to work without a licence
  code. The code is passed to its constructor, and the bundle supplies it through
  `.lurawave.license` in its `IJ_Prefs.txt`.
* **Rung.** This is rung 2 of the sourcing ladder. The decoder is proprietary, so it was decompiled
  and read to write this document, and it was run on a JVM as the oracle. None of its code was
  carried over. The tables and coefficients in "Colour modes" are constants and are copied exactly.
* **Real files.** Three vendor files exist: `fisch.lwf` (embedded, quality 89), `TIGER.LWF` (the same
  picture as four baseline stripes, quality 76) and `karte.lwf` (embedded, quality 100, 345x312
  padded to 384x320). Sources are the SmartCompress 3 Lite package and
  `https://sembiance.com/fileFormatSamples/image/luraWave/`. All three use colour mode 2. All three
  decode bit-exactly against the oracle.
* **Constructed files.** The other modes and options had no real samples. To cover them, streams were
  written with this package's encoder and decoded by the oracle. That covers every colour mode, both
  stripe layouts, an interest region, the verbatim tail and a password-limited public quality. The
  oracle's decodes are committed as test expectations.

## Container

The file starts with `57 56 02 00`. `WV` is the signature, `02` is the only stream version the
decoder accepts, and the fourth byte is ignored. A sequence of chunks follows. Each chunk is a
two-character tag, then a big-endian `u16` length that counts the four tag and length bytes, then
the body. Unknown chunks are skipped. All integers are big-endian.

| Tag | Body | Meaning |
| --- | --- | --- |
| `GE` | `u32 width`, `u32 height`, `u8 mode`, `u8` (ignored) | Visible size and colour mode (see below). |
| `CB` | `u16 codedWidth`, `u16 stripeHeight` (both `u32` when the body is longer than 9 bytes), `u8 levels`, `u8` (ignored; LuraTech writes `0x20`), `s8 lowestPlane` (`-1` means 2), `u16 stripeCount` | Coding geometry. |
| `QU` | `u16 quality` (0..1024) | Encoder quality, informational only. `round(q/1024*100)` is the percentage the vendor reported. |
| `ST` | `u32` byte length of each stripe | Stripe sizes. Each size includes the stripe's terminator word. |
| `DP` | `u32 x`, `u32 y` | Resolution in dots per inch, 16.16 fixed point. Default 72. |
| `MV` | `u32 maximum` | Largest sample of the wide modes. |
| `PW` | `u16 check`, `u16 publicQuality` | Password protection. See "Pass budget". |
| `ER` | `u8 count`, `u8 shift`, then per region `u32 x`, `u32 y`, `u16 w`, `u16 h` | Interest regions. Every region gets the same extra shift. |
| `DT` | padding | Last chunk. The coded data starts right after it, and must start on a multiple of 4. |

`DP` has a default of 72 dpi. `PW` has a default public quality of 1024.

## Coefficient grid

The image is coded on a grid `codedWidth × (stripeHeight · stripeCount)` and cropped to the visible
size after decoding. Both grid dimensions are multiples of `2^levels`. LuraTech's padding content is
irrelevant to a decoder. This writer repeats the last row and column.

Channels are interleaved: sample `(x, y, c)` sits at index `(y·codedWidth + x)·channels + c`.

The wavelet is LeGall's reversible integer 5/3, kept in place:

* After `l` levels, the low-pass samples are the points whose coordinates are both multiples of `2^l`.
* The level-`l` details sit at odd multiples of `2^(l-1)`. HL is odd in x, LH is odd in y, and HH is
  odd in both.

For synthesis, run levels `L … 1`. At step `s = 2^(l-1)`, first undo every column (sampled every `s`
rows), then every row:

* `even[k] -= (odd[k-1] + odd[k] + 2) >> 2`, with `odd[-1] = odd[0]`.
* Then `odd[k] += (even[k] + even[k+1] + 1) >> 1`, with `even[n] = even[n-1]`.

Analysis is the mirror image: rows first, then columns, predict before update.

**Stripes.** A stream with more than one stripe ("baseline", as opposed to "embedded") codes each
`stripeHeight`-row band of coefficients independently. The wavelet still runs over the whole grid.
The decoder's line buffers assume `stripeHeight = 2^levels`. Other stripe heights overwrite one
another, so they are refused.

## Words and bits

Coded data is read as big-endian 32-bit words, most significant bit first.

* A word `FFFFFFFF` ends a stripe. The remaining bits of the stripe read as 0.
* A word `FFFFFF00` or `FFFFFFFE` escapes the following word, which is data even if it looks like a
  control word.
* Any other `FFFFFFxx` word is filler and carries no bits.
* When the data runs out, all further bits are 0.

Before every stripe after the first, the reader drops buffered bits and discards words up to and
including the next terminator.

## Coding one stripe

The first five bits, least significant first, give the top plane `n`. The threshold mask starts at
`m = 2^n | 2^n>>2 | 2^n>>3`. Each bit plane halves `m` (an arithmetic shift) before its passes. The
extra low bits in `m` are the reconstruction offset. A coefficient that turns significant is set to
the whole (shifted) mask.

Coefficients are sign and magnitude (bit 31 is the sign) until the stripe ends. At that point the
negative ones become two's complement.

### Trees

There is one tree per `2^L × 2^L` block and channel. The roots are ordered by channel, then row,
then column. A root carries the level field `λ = L−1` and holds:

* the final low-pass sample, and
* the three level-`L` details at `+dx·2^λ`, `+dy·2^λ` and both, where `dx = channels` and
  `dy = channels·codedWidth`.

Under a root hang three quadtrees, one per orientation. Every node with level field `lv` stands for
four same-orientation coefficients `p, p+dx·2^lv, p+dy·2^lv, p+(dx+dy)·2^lv`:

* An orientation tree's top sits at `+dx·2^(λ−1)`, `+dy·2^(λ−1)` or both from its root, and also has
  level `λ`.
* The four children of a node at level `lv` start at `p − (dx or dy or both)·2^(lv-2)`, according to
  orientation, and are spaced `·2^lv`.
* A node at level 1 is a leaf.

Each node has a **shift**:

* Roots get `1`, plus `1` for channel 0 of modes 6 and 8, or `2` for channel 0 of modes 7 and 19.
* The HL and LH trees inherit the root's shift. The HH tree gets one less.
* Interest regions add their shift. A leaf counts as inside a region when one point of its 4×4
  footprint lies inside the inclusive rectangle: `(x+3, y+2)` for HL, `(x+2, y+3)` for LH, or
  `(x+3, y+3)` for HH. An inner node takes the largest value below it, and the root takes the
  largest over its whole tree.

A coefficient's threshold is `m >>> e`, where `e` is:

* `shift + lv − 1` for a node's four coefficients;
* `λ + shift` for the root's HL and LH details;
* `λ − 1 + shift` for the root's HH detail;
* `λ + 1 + shift` for the root's low-pass sample.

A test is skipped entirely when its threshold is 0.

### Passes

All nodes worth visiting are threaded on one list. The list starts as all roots in order, and nodes
are appended when they open. Each bit plane makes three passes over the list, in this order:

1. **Significance.** For every listed node, test each of its coefficients that is still 0: one bit
   says it reaches the threshold, and a second gives its sign. A root's three details are tested only
   once its trees exist.
2. **Sorting.** This pass opens nodes:
   * An unopened root spends one bit on growing its three orientation trees. If the bit is set, it
     also tests its three details at once.
   * An unopened inner node spends one bit (even when its own threshold is 0). If the bit is set, its
     four children are created, its own four coefficients are tested, and the node is appended to the
     list.
   * An unopened leaf spends one bit only when its threshold is non-zero. If the bit is set, its
     coefficients are tested and the leaf is appended.
   * An opened node with unopened children spends one bit on "some child opens now". After a yes, it
     tries each unopened child in this pass and in every later pass until all are open.

   Nodes appended during this pass are visited in the same pass.
3. **Refinement.** Every coefficient whose magnitude exceeds its threshold `t` gets
   `v &= ~(t>>1)`, then `v |= t` if the next bit is set.

Planes run while `plane > lowestPlane`. After them the mask halves once more, and `L−1` verbatim
rounds follow. Round `r` visits, for every root, first all HL trees, then all LH trees, then all HH
trees, and goes one tree level deeper each round. An inner node not yet expanded is expanded without
spending a bit. For each node's four coefficients and each pattern `b = t, t>>1, …, 1`, one bit sets
or clears the bits of `b`. Then each coefficient with `0 < |v| < 2t` reads a sign bit.

### Pass budget

Every pass, and every verbatim round, counts one. A quality `q` buys this many passes:

| Quality | Passes |
| --- | --- |
| `q ≤ 512` | `n/2−1 + (n − (n/2−1))·q/512` |
| `512 < q ≤ 900` | `n + n·(q−512)/388` |
| above 900 | `2n + n·(q−900)/124` |

The computation uses integer division. Decoding everything means `q = 1024`, which gives `3n`.

Once the count reaches the budget of the `PW` chunk's public quality, the stream carries 32 bits of a
password check, least significant first. Without a password the check value is 0. If any bit
differs, decoding stops. A stream with a password therefore decodes at its public quality only.

When the budget is spent, or the data runs out, the passes stop. The verbatim rounds then resume, but
only after skipping one round.

## Colour modes

`c0…c3` are the decoded channel samples. Every output is clamped to 0..255.

| Mode | Channels | Pixel |
| --- | --- | --- |
| 1 | 1 | `grey = c0+128` |
| 2 | 3 | `Y = (c0>>2)+128`, `R = Y−c2`, `G = Y+((c1+c2)>>1)`, `B = Y−c1` |
| 4 | 3 | `R,G,B = c0,c1,c2 + 128` |
| 6 | 3 | `G = c0+128 − ((c1+c2)>>2)`, `R = c1+G`, `B = c2+G` |
| 7 | 4 | Mode 6 for `r,g,b`, `k = c3+128`, then the quadratic below |
| 8 | 3 | `l = clamp(c0+128, 0, 255)`, `a,b = clamp(c1,c2, −128, 127)`, then the quadratic below |
| 17 | 1 | 16-bit `c0`; the 8-bit view is `c0 >> s` |
| 19 | 3 | Mode 6 on `c >> s`, without the +128 |
| 22 | 1 | 16-bit `c0 + max/2`; the 8-bit view is `(c0 + max/2) >> s` |

For mode 17 and mode 22, `s` is the number of halvings that bring the `MV` maximum to 256 or less.
Without `MV`, the 8-bit views of modes 17 and 22 assume a maximum of 65535 (`s = 8`). Mode 19 keeps
`s = 0`, and the 16-bit view of mode 22 adds nothing.

Mode 7, with every product `>> 4` (`rr = r·r>>4`, and so on), and every sum `>> 20`:

* `R = −52830·rr + 24977·rg + 57294·rk − 12674·kg + 20552·kb − 17889·gb + (51983·r<<4) − (12151·b<<4)`
* `G = −12660·rr − 27110·gg + 9223·rk + 49218·kg + 15005·kb + (14458·r<<4) + (22478·g<<4) − (10849·b<<4)`
* `B = −24695·bb − 16732·rb + 13983·kg + 55580·kb + 9221·gb + (23589·b<<4)`

Mode 8, with plain products:

* `R = ((20104·l²>>2) − 16115·a² − 25325·b² + (25846·al>>1) + (8469·bl>>1) − 27977·ab + (41231·a<<7) + (23799·b<<7) + (46573·l<<6)) >> 22`
* `G = (−13522·a² + 6650·b² + (−21507·al>>1) + (62429·l<<6)) >> 22`
* `B = ((−48443·b<<1) + 62757·l) >> 16`

## What this implementation refuses, and why

* **More than one interest region.** The vendor decoder seeds the second and later regions into the
  next tree's nodes, and throws at the last tree. No file with two regions decodes there at all.
* **Stripe heights other than `2^levels`.** The stripe buffers would overlap, as described above.
* **A single wavelet level.** The root sits at level 0, so the vendor's shifts go negative.
* **Coded data shorter than its `ST` chunk declares, or a stripe whose terminator is missing.**

## Writing

The writer produces lossless, embedded streams:

* mode 1 for 8-bit grey, mode 17 (`MV = 65535`) for 16-bit grey, and mode 6 for colour;
* up to six levels, `lowestPlane = 0`;
* no regions and no password.

The encoder runs the decoder's traversal. Wherever the decoder would read a bit, the encoder answers
the question that bit asks from the true coefficients:

* is the coefficient at least the threshold's top bit;
* is the bit of the coefficient under the threshold's top bit set;
* does anything below this node turn significant at this plane.

Coefficients that become significant late are thereby impossible. Every coefficient is found exactly
at its own most significant bit and refined down to bit 0, so the decode is exact. The oracle returns
the source pixels unchanged for every committed writer fixture.

## Unknown

* The byte after the colour mode in `GE`, the byte after the level count in `CB`, and the fourth
  header byte. The decoder ignores all three.
* How the `PW` check value is derived from a password. The decoder hashes the password string, but
  no password is accepted here.
* What colour models LuraTech meant by modes 7 and 8. CMYK and an opponent space are guesses from
  the formulas.
* Whether other encoders exist that write anything the decoder handles differently. The only real
  samples are mode 2.
