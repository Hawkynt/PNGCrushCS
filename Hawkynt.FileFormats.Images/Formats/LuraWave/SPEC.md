# LuraWave (`.lwf`) — derived specification

LuraTech never published the LuraWave bitstream. This document is a description written from
observation of LuraTech's own encoder and decoders. Every statement here was checked against LuraTech's C
decoder's output; where its Java decoder differs, the section "LuraTech's Java decoder" says how.

## Provenance

* **Reference codec.** LuraWave SmartCompress 3.0 Lite (`LuraSmrt.exe`, 2002-10-08), the freeware tool LuraTech
  shipped as `LWF_SmartCompress3_win.exe`. Its installer does not run under Wine, but its files extract with 7-Zip and
  cabextract. The LWF codec is compiled into the program itself (the `Lwf153d.dll` beside it is the older, unchunked
  1998 format and only lends the program a few helpers). A harness DLL that stands in for `Lwf153d.dll` calls the
  codec's internal entry points once the program has loaded: encoder set-up `0x455730` and stripe encoder
  `0x4557C0`, decoder header `0x4554C0`, set-up `0x4555B0` and decode `0x455670`. Decompiled with Ghidra wherever
  behaviour had to be explained. This is the oracle.
* **Second decoder.** `com.luratech.lwf.lwfDecoder` in `lwf_jsdk2.6.jar` (one class, dated 2002-04-24),
  redistributed under licence by McMaster University in the MBF ImageJ bundle
  (`https://imagej.net/ij/plugins/mbf/mbf-plugins.zip`). It needs a licence code, which the bundle supplies through
  `.lurawave.license` in its `IJ_Prefs.txt`. It was the first oracle. Where it disagrees with the C codec it is
  wrong for LuraTech's own streams, as described below.
* **Rung.** This is rung 2 of the sourcing ladder. Both programs are proprietary. They were decompiled and run to write
  this document, and none of their code was carried over. The tables and coefficients in "Colour modes" and the
  password hashes are constants and are copied exactly.
* **Real files.** Three vendor files exist: `fisch.lwf` (embedded, quality 89), `TIGER.LWF` (the same picture as four
  baseline stripes, quality 76) and `karte.lwf` (embedded, quality 100, 345x312 padded to 384x320). Sources are the
  SmartCompress 3 Lite package and `https://sembiance.com/fileFormatSamples/image/luraWave/`. All three use colour
  mode 2 and decode exactly as the C codec decodes them.
* **LuraTech-encoded files.** The C encoder wrote streams from synthetic gradients with noise. They cover every
  colour mode it writes, 3 to 6 levels, both stripe layouts, one and two interest regions, byte budgets, a reduced
  quality, passwords, 12-bit and 16-bit maxima, and 37x23, 100x70 and 300x200 pictures. Every one decodes exactly as
  the C decoder decodes it, with and without the password. A selection is committed with the decoder's output.
## Container

The file starts with `57 56 02 00`. `WV` is the signature, and `02` is the only stream version the
decoders accept. LuraTech's encoder always writes 0 for the fourth byte, and its decoders ignore it. A sequence of chunks follows. Each chunk is a
two-character tag, then a big-endian `u16` length that counts the four tag and length bytes, then
the body. Unknown chunks are skipped. All integers are big-endian.

| Tag | Body | Meaning |
| --- | --- | --- |
| `GE` | `u32 width`, `u32 height`, `u8 mode`, `u8 flags` | Visible size and colour mode (see below). Bit 0 of `flags` marks the striped ("baseline") layout. The encoder sets it when it writes more than one stripe and clears it otherwise. The C decoder relies on it, and the Java decoder ignores it. |
| `CB` | `u16 codedWidth`, `u16 stripeHeight` (both `u32` when the body is longer than 9 bytes), `u8 levels`, `u8` (always `0x20`; ignored), `s8 lowestPlane` (`-1` means 2), `u16 stripeCount` | Coding geometry. LuraTech's encoder always writes `-1`. |
| `QU` | `u16 quality` (0..1024) | Encoder quality, informational only. `round(q/1024*100)` is the percentage the vendor reported. |
| `ST` | `u32` byte length of each stripe | Stripe sizes. Each size includes the stripe's terminator word. |
| `DP` | `u32 x`, `u32 y` | Resolution in dots per inch, 16.16 fixed point. Default 72. |
| `MV` | `u32 maximum` | Largest sample of the wide modes. |
| `PW` | `u16 check`, `u16 publicQuality` | Password protection. See "Passwords". |
| `ER` | `u8 count`, `u8 shift`, then per region `u32 x`, `u32 y`, `u16 w`, `u16 h` | Interest regions. Every region gets the same extra shift. |
| `PR` | first piece `u32 total`, then bytes | Application data of up to `0x6FFFFFFE` bytes, split into chunks of at most `0x7FBC` bytes. Only the first chunk carries the total. SmartCompress never writes it. Skipped. |
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

**Stripes.** A stream with more than one stripe (`GE` flag bit 0, "baseline", as opposed to "embedded") codes each
`stripeHeight`-row band of coefficients independently. The wavelet still runs over the whole grid.
The decoder's line buffers assume `stripeHeight = 2^levels`. Other stripe heights overwrite one
another, so they are refused.

## Words and bits

Coded data is read as big-endian 32-bit words, most significant bit first.

* A word `FFFFFFFF` ends a stripe. The remaining bits of the stripe read as 0.
* A word `FFFFFF00` or `FFFFFFFE` escapes the following word, which is data even if it looks like a
  control word.
* Any other `FFFFFFxx` word is filler and carries no bits.
* When the data runs out, all further bits are 0, and the stripe ends after the current pass. A refinement bit read
  past the end leaves its coefficient unchanged.

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
   * Before trying them, a node checks whether the thresholds below it have all run out: that is when
     `m >>> minShift == 0`, where `minShift` is the smallest shift of any leaf below the node. Without regions this
     is the node's own shift, or the root's shift minus 1. If they have run out, the node gives up its unopened
     children without spending a bit, now and in every later pass. In practice this only matters for a first
     channel weighted by two planes (modes 7 and 19), or for a lowest plane below 2.

   Nodes appended during this pass are visited in the same pass.
3. **Refinement.** Every coefficient whose magnitude exceeds its threshold reads one bit. Let `T` be the
   threshold's top bit. Then `v = (v & ~(T | T>>1)) | T>>3`, and `v |= T` if the bit is set.

Planes run while `plane > lowestPlane`. If the budget runs out, or the data does, the stripe ends there.
Otherwise the mask halves once more and the verbatim stage follows. It has `L−1` rounds, and each round makes
three steps: all HL trees, then all LH trees, then all HH trees. Each step visits every root and goes one tree
level deeper per round. An inner node not yet expanded is expanded without spending a bit. For each node's four
coefficients and each pattern `b = t, t>>1, …, 1`, one bit sets or clears the bits of `b`. Then each coefficient
with `0 < |v| < 2t` reads a sign bit.

### Pass budget

Every bit-plane pass counts one. A quality `q` buys this many passes:

| Quality | Passes |
| --- | --- |
| `q ≤ 512` | `n/2−1 + (n − (n/2−1))·q/512` |
| `512 < q ≤ 900` | `n + n·(q−512)/388` |
| above 900 | `2n + n·(q−900)/124` |

The computation uses integer division. Decoding everything means `q = 1024`, which gives `3n`.

The verbatim stage counts differently. Its count starts at 1, with `left` being the passes still unspent; if
`left` is exactly 1, the stage is skipped. Step `o` (0, 1, 2) of round `r` counts only when
`3L − 3r − o ≤ 3·p − 2`, where `p` is the plane the bit-plane loop stopped at (the lowest plane, or the top plane
if that is lower). The stage ends once the count reaches `left`. For a lossless stream this budget never runs
short.

### Passwords

The `PW` chunk holds a 16-bit check of the password and the public quality. Without the password, a stream
decodes to its public quality: the public quality sets the budget, and no check is read.

With a password, the decoder first compares its 16-bit hash with the chunk and refuses a mismatch ("wrong
password"). Then it decodes at full quality. When the pass count reaches the public budget, which can happen in
the verbatim stage too, the stream carries the 32-bit hash, least significant bit first. A mismatch there is an
error.

Both hashes look at the first 32 characters. A character "counts" if it is a letter, a digit, a space, or one of
`+-/*!"$%&()=?_`:

* 32-bit: `h = h·33 + c` for a counting character `c`, `h = h·33 + 0x4D` otherwise; 0 becomes `0x12674451`.
* 16-bit: `h = h·5 + c/3` for a counting character, `h = (h+13)·5` otherwise, modulo 2^16; 0 becomes `0x1645`.

## Colour modes

`c0…c3` are the decoded channel samples. The names in quotes are the ones SmartCompress lists for each mode.

| Mode | Channels | Pixel |
| --- | --- | --- |
| 1 | 1 ("8 bit grey scale") | `grey = c0+128` |
| 2 | 3 ("3x8 bit YUV") | `y = c0>>2`, `r = y−c2`, `b = y−c1`, `g = (c0−r−b)>>1`, each `+128` |
| 4 | 3 ("3x8 bit raw") | `R,G,B = c0,c1,c2 + 128` |
| 6 | 3 ("3x8 bit YUV") | `G = c0+128 − ((c1+c2)>>2)`, `R = c1+G`, `B = c2+G` |
| 7 | 4 ("4x8 bit CMYK") | Mode 6 for `r,g,b` (cyan, magenta, yellow), `k = c3+128`, then the quadratic below |
| 8 | 3 ("3x8 bit LAB") | `l = clamp(c0+128, 0, 255)`, `a,b = clamp(c1,c2, −128, 127)`, then the quadratic below |
| 17 | 1 ("16 bit grey scale") | 16-bit `clamp(c0, 0, M)` |
| 18 | 3 ("3x16 bit YUV") | Mode 2 without the `+128`, each then `>> s` |
| 19 | 3 ("3x16 bit YUV") | Mode 6 without the `+128`, each then `>> s` |
| 22 | 1 ("16 bit grey scale") | 16-bit `clamp(c0 + M/2, 0, M)` |

Every 8-bit output is clamped to 0..255. `M` is the `MV` chunk's maximum, or 65535 without one. For the 8-bit
views of the wide modes, a maximum of exactly 32768 counts as 32767. `s` is the bit length of that maximum (at
least 8, at most 16) minus 8.

The encoder takes these inputs: grey, R/G/B in three byte orders (modes 2, 4, 6), CMYK with the transform on C, M
and Y (mode 7), L*a*b* passed through (mode 8), and 16-bit equivalents (modes 17 to 22).

Mode 7, with every product `>> 4` (`rr = r·r>>4`, and so on), and every sum `>> 20`:

* `R = −52830·rr + 24977·rg + 57294·rk − 12674·kg + 20552·kb − 17889·gb + (51983·r<<4) − (12151·b<<4)`
* `G = −12660·rr − 27110·gg + 9223·rk + 49218·kg + 15005·kb + (14458·r<<4) + (22478·g<<4) − (10849·b<<4)`
* `B = −24695·bb − 16732·rb + 13983·kg + 55580·kb + 9221·gb + (23589·b<<4)`

Mode 8, with plain products:

* `R = ((20104·l²>>2) − 16115·a² − 25325·b² + (25846·al>>1) + (8469·bl>>1) − 27977·ab + (41231·a<<7) + (23799·b<<7) + (46573·l<<6)) >> 22`
* `G = (−13522·a² + 6650·b² + (−21507·al>>1) + (62429·l<<6)) >> 22`
* `B = ((−48443·b<<1) + 62757·l) >> 16`

Channel 0 is coded one plane finer in modes 6 and 8, and two planes finer in modes 7 and 19. Mode 20 ("3x16 bit
LAB") is coded one plane finer, and mode 21 ("4x16 bit YUVK") two.

## What this implementation refuses, and why

* **Modes 5, 20 and 21.** These are four plain 8-bit channels, "3x16 bit LAB" and "4x16 bit YUVK". LuraTech's
  encoder writes them, but its C decoder only hands back their raw channels, and the Java decoder rejects them.
  No LuraTech program shows what these pixels look like in RGB.
* **Stripe heights other than `2^levels`.** The stripe buffers would overlap, as described above. LuraTech's encoder
  never writes any other height.
* **A single wavelet level.** The root sits at level 0, so the shifts go negative. LuraTech's encoder crashes at two
  levels on small pictures.
* **Coded data shorter than its `ST` chunk declares, or a stripe whose terminator is missing.**
* **A wrong password.** The reader throws instead of falling back to the public quality, the way SmartCompress
  shows "the password was wrong".

## Writing

The writer produces lossless, embedded streams:

* mode 1 for 8-bit grey, mode 17 (`MV = 65535`) for 16-bit grey, and mode 6 for colour;
* up to six levels, `lowestPlane = 2` as in LuraTech's files (0 below three levels);
* no regions and no password.

The encoder runs the decoder's traversal. Wherever the decoder would read a bit, the encoder answers
the question that bit asks from the true coefficients:

* is the coefficient at least the threshold's top bit;
* is the bit of the coefficient under the threshold's top bit set;
* does anything below this node turn significant at this plane.

Coefficients that become significant late are thereby impossible. Every coefficient is found exactly
at its own most significant bit and refined down to plane 2, and the verbatim stage delivers the rest, so the
decode is exact. Sorting down to plane 0 would be about two percent smaller. But then nodes whose thresholds have
run out appear, and the two LuraTech decoders read those differently. At plane 2, with channel weights of at most
one, they cannot appear. Both decoders return the source pixels unchanged for every committed writer fixture.

## LuraTech's Java decoder

It agrees with the C codec on most streams. Where they differ, it is wrong for LuraTech's own streams:

* Refinement uses `v &= ~(t>>1)` and then `v |= t` on a set bit, with the whole threshold mask `t`. That lacks the
  `T>>3` offset, so truncated streams come out slightly different.
* Mode 2 green is `y + ((c1+c2)>>1)` from the rounded luma. That throws away `c0 & 3`, so a lossless stream is
  off by up to 2.
* It does not give up spent children, so it reads extra bits and mis-decodes LuraTech's streams in modes 7 and 19.
* After a spent budget or a failed password check it starts the verbatim stage again one round later.
* It reads the `MV` default differently: no bias in mode 22, and no shift in the mode 19 view. It shifts before the
  colour transform in mode 19.
* It hashes a password with its last character replaced by a zero byte (`String.getBytes(0, length−1, …)`). So it
  never accepts the right password for a protected stream.
* It throws on more than one interest region, and rejects modes 5, 18, 20 and 21.

## Unknown

* Whether other encoders exist that write anything the decoders handle differently. The only encoder found is
  SmartCompress 3.0's.
* What filler words (`FFFFFFxx` other than `00`, `FE` and `FF`) were meant for. The encoder never
  writes them, and the two decoders skip them differently.
