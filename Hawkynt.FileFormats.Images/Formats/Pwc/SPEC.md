# PWC — the piecewise-constant image model, bitstream as written by pwcencode 1.5.1

This is a specification derived for this repository. No bitstream description of PWC was ever
published: Paul Ausbeck's papers describe the model, and his 1999 demonstration package ships only
Win32 binaries (`pwcencode.exe`, `pwcdecode.exe`, `pwsview.exe`) and a static streaming library
(`pwc_lib.zip`: `pwc.lib` plus its public headers). Everything below was established from

* the papers — *Context Models for Palette Images* (DCC 1998), *A Streaming Piecewise-Constant
  Model* (DCC 1999), DCC 2000, SPIE 1998, and *The Piecewise-Constant Image Model* (Proc. IEEE
  88(11), 2000) — for the vocabulary (Q0 skip, Q1 edges, Q2 corner guesses, Q3 guesses, Q4 colour,
  Q5 list membership), the edge model, LOCO-I prediction and the Speck-style count coder;
* the public headers of `pwc_lib.zip` (`ifilelib.h`, `pwcfile.h`, `pwc.h`) for the container;
* reading `pwc.lib` (MSVC COFF objects, symbols intact) and `pwcdecode.exe` as evidence of the
  decision order, the context indices and the state updates;
* and, as the arbiter, the two programs themselves run under Wine: every claim below is pinned by
  test vectors produced with `pwcencode.exe` and decoded with `pwcdecode.exe`.

The implementation beside this file decodes all fifteen pictures of the distribution and all
153 option/geometry vectors made for this work exactly as `pwcdecode.exe` does, and its encoder
reproduces every one of those files byte for byte.

All multi-byte integers are little-endian.

## 1. Container

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | Signature `4yVa` (`0x61567934` as a 32-bit word) |
| 4 | 2 | Rows (height) |
| 6 | 2 | Columns (width) |
| 8 | 1 | Depth: 1, 4 or 8 bits per pixel. Each selects a different model (§3, §4). |
| 9 | 1 | Bits 0–2 "uses": 1 = palette stored, 2 = colour, 4 = alpha (never acted on). Bit 3: rows coded bottom-up (`-flip`). Bits 4–7 reserved. |
| 10 | 2 | Reserved, 0 |

When the palette bit is set, a palette follows: a 16-bit entry count, then that many
**blue, green, red** triplets (no pad byte). The reference library ignores palettes of more than 256
entries. Without a palette the decoder assumes a linear one: depth 8 → grey `i`; depth 4 → grey
`8 + 16·i`; depth 1 → index 0 white, index 1 black.

Then the four-byte option word:

| Byte | Meaning |
| --- | --- |
| 0 | Flags: `0x01` grey (prediction only), `0x02` no corner guesses (`-noq2`), `0x04` no list decision (`-noq5`; the encoder's default unless the palette has all 256 colours) |
| 1 | `maxlps`, the adaptation limit (default 7) |
| 2 | 16 (written, never read) |
| 3 | 0 |

The rest of the file is one arithmetic-coded stream (§2) holding all rows in coding order. There is
no length field, chunk marker, row index or trailer.

The number of colours given to the colour coder (§2.3) is the palette's entry count, or `1 << depth`
without a palette; `n = colours − 1`, and `n < 1` is replaced by 255.

## 2. Entropy coding

### 2.1 Binary arithmetic coder

A carry-free coder over a 16-bit interval `[low, high]`, both held in 32-bit unsigned registers whose
upper bits are never cleared (they always agree, so they cancel in every comparison and difference;
an implementation must keep 32-bit wrap-around arithmetic to stay bit-exact).

* Encoder start: `low = 0`, `high = 0xFFFF`. Decoder start: `low = high = value = 0`.
* **Before** every decision, renormalise: while `(high & 0xFFFFFF00) <= low` — the ends agree above
  their lowest byte — the encoder emits `(high >> 8) & 0xFF`, the decoder does
  `value = value << 8 | next byte`; then both do `low <<= 8`, `high = high << 8 | 0xFF`. (The
  decoder therefore reads two bytes before its first decision.)
* Split: `split = (((p + 1) · (high − low)) mod 2³²) >> 8 + low`, with `p` from §2.2.
* Bit 1 ⇔ `value > split`; then `low = split + 1`. Bit 0: `high = split`.
* Flush: the encoder writes `low >> 8` and `low` (low byte of each).

A decoder consumes exactly every byte of a stream its encoder produced, and ends with
`value ≡ low (mod 2¹⁶)`. The reference decoder does not check this; this implementation does, and
refuses a stream that fails it as corrupt.

### 2.2 Statistics and the split table

Every context is a pair of bytes: `zeros` (zeros seen, plus one) and `total` (decisions seen, plus
two); a fresh context is (1, 2). The share of the interval given to zero is
`p = table[zeros][total]`, where the shipped 64 KiB table equals

    table[z][t] = 255                                  if z = 0 or z ≥ t
                = max(0, ⌊(512·z + t) / (2·t)⌋ − 1)    otherwise

(a rounded `256·z/t − 1`; the SHA-256 of the original table is pinned by a test).

Two update rules exist; each context uses one of them for its whole life.

* **Adaptive** (all model decisions): `total += 1`; on a zero `zeros += 1`; then if
  (`zeros > maxlps` and `total − zeros > maxlps`) or `total = 255`, halve:
  `total = ((total − 1) >> 1) + 1`, `zeros = ((zeros − 1) >> 1) + 1`.
* **Fixed** (the colour coder of §2.3): `total += 1` in a byte; if that wrapped to 0, rebuild
  `ones = ((256 − zeros) mod 256) >> 1`, `zeros = ((zeros + 1) mod 256) >> 1`,
  `total = ones + zeros + 1`; then on a zero `zeros += 1`.

### 2.3 Colour coder (Q4)

Codes a value `v ∈ [0, n]` around a prediction `q` under an activity class `a`.

**Folding to a rank.** With `room = n − q`:
if `q < room`: `d = v − q`; rank is `v` when `d > q`, else `2d` for `d ≥ 0`, `−2d − 1` for `d < 0`.
Otherwise `d = q − v`; rank is `d + room` when `d > room`, else `2d` for `d ≥ 0`, `−2d − 1` for
`d < 0`. (Decoding inverts this; rank 0 with `q ≤ n` is `q`.)

**Blocks.** Let `C` = bit length of `n`. For classes `c = 0 … C`, with `count` starting at 0 and
`size` at 1: `count = (max(count + 1, 8) + 1) & ~1`; if `⌊n / size⌋ < count` or `c = C`, then
`count = ⌊n / size⌋ + 1`; create `count` blocks of `size` ranks for class `c`; then
`count >>= 1`, `size <<= 1`. Each class's blocks form a chain. Then for `c = 1 … C` the chain of
class `c − 1` is spliced onto class `c`: walking two blocks of `c − 1` per block of `c`, if class
`c − 1` has an even number of blocks its last one is followed by the block of class `c` that starts at
the same rank; with an odd number the chain of `c − 1` simply ends. Chains thus share their tails,
and with them their statistics.

A block of `s` ranks carries one *escape* context and `s − 1` tree contexts, laid out as a pre-order
binary tree: at a node of half-width `h`, bit 0 moves to the next context, bit 1 skips `h` contexts
and adds `h` to the offset.

**Initial statistics**, per class chain, walking with `remaining = n + 1`: each block gets escape
(1, 2) and tree contexts (4, 8) until the block holding the last rank; that block's escape becomes
(1, 1), and along its tree, wherever `h ≥ remaining` the node becomes (1, 1) and the walk goes left,
else `remaining −= h` and it goes right. (1, 1) predicts zero with certainty.

**Coding** (`a` clamped to `C`): starting at the head of class `a`'s chain, code escape = 1 while the
rank lies beyond the block (subtracting its size), then 0; then the offset inside the block MSB first
through its tree. All of these use the *fixed* update.

## 3. Depth 1: the bilevel model

Two history rows of `width + 4` bytes, initially zero: `R1` (the row above, overwritten in place by
the row being coded) and `R2` (the row above that). `R1[width + 2] = 1`. 0x481 adaptive contexts.

The context of pixel `x` has ten bits, low to high: `P(x−1)`, `P(x−2)`, `R1[x+2]`, `R1[x+1]`,
`R1[x]`, `R1[x−1]`, `R1[x−2]`, `R2[x+1]`, `R2[x]`, `R2[x−1]` (pixels outside the row count as 0 on
the left; on the right they are whatever the buffers hold). At the start of a row it is built from
`R2[0..1]` and `R1[0..2]` only. After coding pixel `x` as `b`:
`ctx = R2[x+2]<<7 | R1[x+3]<<2 | (ctx & 0x1BD)<<1 | b`, then `R2[x] = R1[x]`, `R1[x] = b`.

A skip limit `L` is set at the start of each row to `width` if the previous row was skipped whole
(and for the first row), else 0.

At each pixel, if the context is 0 or 0x3FF (all white / all black, colour `c`), make a **skip
decision** (Q0); otherwise code the pixel under its context.

**Skip.** If `x < L`, the run is `n = L − x`. Otherwise find the first `r ≥ 0` with
`R1[x+3+r] ≠ c` or `R2[x+2+r] ≠ c`; set `L = x + 1 + r` and `n = r + 1`. Let `k` = bit length of
`n − 1`. Code "the next `n` pixels are all `c`" under context `0x400 + 16c + k`.

* Yes: emit `n` pixels of `c`. If `n = width` the next row starts with `L = width`; otherwise
  `x += n` and `ctx = R2[x+1]<<7 | R1[x+2]<<2 | (ctx & 0x1BC)<<1 | (ctx & 3)`. The history rows are
  not touched (they already hold `c` there).
* No: code the run length `m < n` MSB first over bits `k−1 … 0`: a bit of weight `w` is coded only if
  `m_so_far + w ≤ n`, under context `0x420 + 32c + (16 once a one has been coded) + bit index`.
  Emit `m` pixels of `c`, advance `x += m` with the same context rebuild as above, then emit `1 − c` as
  an ordinary pixel update at `x` (no decision: it is implied).

Rows are coded top to bottom (bottom to top with the flip bit) and each holds exactly `width` pixels.

## 4. Depths 4 and 8: the region model

### 4.1 State

* A colour row and an edge row, each `width + 8` bytes with column 0 at offset 4, zero-initialised;
  the edge row holds 3 at column `width + 1`. Both are rewritten in place, so ahead of the cursor
  they describe the row above. Edge byte bit 0: pixel differs from its left neighbour; bit 1: differs
  from the pixel above.
* 0x331 adaptive edge contexts; for depth 8 also 0x200 corner contexts (indexed by colour) and 0x100
  list contexts (indexed by the left colour).
* An *impossible* stamp per colour and a running stamp (starting at 1). A colour is impossible while
  its stamp equals the running stamp; incrementing the running stamp clears them all.
* A guess pool: depth 4 — 256 entries filed under 4096 keys; depth 8 — 1024 entries under 256 keys.
  Each entry has a key, a colour and an adaptive context (fresh at start, **kept** when the entry is
  recycled). Entries are ordered by recency (initially entry 0 the least recent, then 1, …); each key
  has an ordered list.
* A row-start flag "whole row follows", initially set.
* The edge context of pixel `x`: `E(x−1) | Eabove(x−1)<<2 | Eabove(x)<<4 | Eabove(x+1)<<6`.

### 4.2 Row start

Edge cursor and colour cursor to column 0; `ctx = Eabove(0)<<4 | Eabove(1)<<6`; skip limit =
`width` if "whole row follows" else 0, and clear the flag; colour row column −1 := column 0, column
`width` := column `width − 1`; corner := 0; stripe start := 0; running stamp += 1; `E(0) = 0`.

On every row but the first, code the vertical edge of pixel 0 under context `0x100 + ctx`: no edge
makes the stripe colour `above(0)`; an edge marks `above(0)` impossible. Then *advance*: after
pixel `x`, `ctx = Eabove(x+2)<<6 | (ctx >> 2 & 0x3C) | E(x)`, both cursors move right.

### 4.3 First row

After the row start, code "every pixel equals its left neighbour" under context `0x301 + k`, `k` =
bit length of `width − 2` (0 for width 1). Yes: set "whole row follows" and close one stripe covering
the row. No: for each `x ≥ 1` code the horizontal edge under `ctx` (only bit 0 can be set in the first
row), close the stripe before `x` (colour unknown) when it is set, advance; finally close the last
stripe.

### 4.4 Other rows

For each `x ≥ 1`:

* If `ctx = 0`, make a skip decision (§4.5), then advance.
* Otherwise `fork = ctx & 0x12` (left differs from above-left; above differs from above-left) and
  code the horizontal edge `h` under `ctx`.
  * `h = 0`: if `fork = 0x12` and `above(x)` is possible and differs from the known stripe colour,
    code the vertical edge `v` under `0x100 + ctx`; if `fork = 0x12` and `above(x)` is impossible, or
    `fork` is 0x02 or 0x10, `v = 1` without a decision; if `fork = 0` (or `above` equals the stripe
    colour), `v = 0`.
  * `h = 1`: close the stripe before `x` with its colour (or unknown); the new stripe's colour is
    unknown. If `fork ≠ 0` and `above(x)` is possible, code `v` under `0x200 + ctx`; otherwise
    `v = 1`.
  * A coded `v = 0` makes `above(x)` the stripe colour; a coded `v = 1` marks it impossible.
  * Advance.

Then close the last stripe.

### 4.5 Skip (Q0)

With the edge cursor at `e`: if `e < limit`, `n = limit − e`; otherwise `n = j − 1 − e` for the first
`j ≥ e + 2` with a non-zero edge byte. `k` = bit length of `n − 1`. Code "pixels `x … x+n−1` each
equal their left neighbour" under context `0x301 + k`.

* Yes: if `n − 1 = width − 2` set "whole row follows" (the edge cursor stays), else the edge cursor
  moves `n − 1`. Fill the stripe so far with its colour, corner := `above(x)`, the colour cursor
  moves `n − 1` and the stripe restarts there (colour unchanged).
* No: code the run length `m` as in §3 under context `0x311 + (16 once a one was coded) + bit index`.
  Move the edge cursor `m` and store 3 there; fill the stripe so far with its colour; corner :=
  `above(x)`; move the colour cursor `m`; a new stripe of unknown colour starts there; running stamp
  += 1 and the closed colour becomes impossible.

### 4.6 Closing a stripe

If its colour is unknown, find it (§4.7), looking at the stripe's first pixel. Then corner :=
`above(end − 1)`, fill the stripe, running stamp += 1, and mark the stripe's colour impossible (the
next stripe differs from it by construction).

### 4.7 Finding a colour

Neighbours of the stripe's first column `s`: `L` = colour row at `s − 1` (already the current row),
`A` = at `s`, `B` = at `s + 1` (row above), `D` = the corner register.

**Depth 4.** Key `A<<8 | D<<4 | L`. For each entry of that key's list in order whose colour is
possible, code "it is this colour" (Q3) under the entry's context; on a hit move the entry to the front
of its list and make it most recent. If none hits, code the colour with the colour coder (prediction
0, the highest activity class), recycle the least recent entry (most recent now; removed from its old
list), set its key and colour, and **append** it to the key's list.

**Depth 8.**
1. Unless flag `0x02`: corner guesses (Q2) — for `D`, then for the row-above colour at
   `s + stripe length`: if possible, code "it is this colour" under its corner context; a hit is the
   colour, a miss marks it impossible.
2. If the list keyed by `L` is non-empty: unless flag `0x04`, code "the colour is in this list" (Q5)
   under list context `L` (the encoder answers for any entry, possible or not); a no skips to step 3.
   Then guess (Q3) through the list in order, skipping impossible colours — with Q5 the last entry is
   not asked but taken when nothing earlier hits; without Q5 an exhausted list goes to step 3. A hit
   moves to the front of its list and becomes most recent.
3. Code the colour with the colour coder around the LOCO-I median prediction of `L`, `A`, `D`
   (`D ≤ min(L,A)` → `max`; `D ≥ max(L,A)` → `min`; else `(L + A − D) mod 256`), activity class = bit
   length of the spread of the prediction's interval widened to include `B`; recycle an entry as for
   depth 4 under key `L`.

### 4.8 Grey mode (flag `0x01`)

Every row: column −1 := column 0, column `width` := column `width − 1`, corner := 0; for every pixel,
code its colour as in §4.7 step 3 at that pixel, then corner := the row-above colour there and store
the new colour. No edges, skips or guesses.

For depth 4 the reference coder has no grey predictor at all: a 4-bit file with the grey flag codes no
decision and decodes to index 0 everywhere. The reader reproduces that; the writer refuses to produce
such a file.

### 4.9 Output

Each coded row is the colour row's columns `0 … width − 1`. Rows are top to bottom unless the flip
bit is set.

## 5. What remains unknown

Nothing that affects files written by pwcencode 1.5.1. Untested corners, implemented as the shipped
code behaves: a depth-1 skip of 32768 pixels or more reaches a context index shared with the run-length
contexts; palettes larger than `1 << depth` at depth 4 are refused here (the reference library would
index past its arrays); the "alpha" and "colour" bits of the header are carried but never interpreted.
Files from versions before 1.5 (two-pass PWC, the 16-bit-count coder of DCC 98) use a different
bitstream and are not covered.
