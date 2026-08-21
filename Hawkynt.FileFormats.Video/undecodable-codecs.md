# Codecs investigated and not implemented

Eight codecs were investigated and none was implemented. That is a result rather than a gap, and it is
written down here so the work is not repeated by somebody who assumes it was never attempted.

They stop in three different places, and the three are worth keeping apart. Five need constant tables
that are not in the file. VP6 and VP5 stop somewhere else entirely: VP6's tables **are** published,
every one of them was transcribed and checked, and the decode still does not come out. Lagarith stops
at a third place again — its wrapper comes out completely, and the entropy coder inside it is defined
by the rounding behaviour of one implementation's floating-point unit rather than by anything written
down. Where each stops is recorded below.

None of the eight had anything committed.

Indeo 3 (`IV32`), Indeo 4 (`IV41`), Indeo 5 (`IV50`), TrueMotion 1 (`DUCK`) and TrueMotion 2 (`TM20`)
are the proprietary codecs of the multimedia era. None has a published specification. No encoder for
any of them exists in ffmpeg, so no sample can be made here; samples were fetched from
`samples.ffmpeg.org` under `V-codecs/IV32/`, `IV41/`, `IV50/`, `DUCK/` and `TM20/`, and ffmpeg decodes
all five, so it remains a usable oracle.

## The argument that settles four of them

Every one of these codecs needs large constant tables — vector-quantisation dyads, delta and vector
tables, quantisation matrices, default variable-length code sets. There are two places such a table
can come from: the file, or the decoder. If it is in the file, the format can be recovered by reading
files. If it is in the decoder, it can only be recovered from somebody else's implementation, and
this project does not transcribe implementations — a copied decoder is a licence problem and teaches
nothing about whether the result is right.

So the question worth asking before writing any code is whether the tables are in the stream. The
smallest frame in each corpus answers it:

| codec | geometry | frames measured | smallest frame |
| --- | --- | --- | --- |
| Indeo 3 | 152x116 – 320x240 | 131–1073 | **340 bytes** |
| Indeo 4 | 240x180 – 360x288 | 904–15363 | **14 bytes** |
| Indeo 5 | 240x180 – 320x240 | 40–782 | **2 bytes** |
| TrueMotion 1 | 288x144 – 336x168 | 430–3196 | **0 bytes** |
| TrueMotion 2 | 320x224, 320x240 | 30–1791 | 92 bytes |

A 340-byte frame for a 320x240 picture, a 14-byte frame, a two-byte frame and an empty frame cannot
carry a quantisation table between them. Those tables are in the codec binary. Four of the five are
therefore not implementable here, and no amount of further effort changes that.

Indeo 3 was mapped anyway, because it was first and because the mapping is worth having if the
question is ever revisited. Its header is fully recovered and verified over roughly 3000 frames in
seven files: a 16-byte outer header (frame number, checksum, the magic `FR` = `0x4652`, data size),
then a 32-byte inner header carrying height at +12 and width at +14 as unsigned 16-bit values — both
confirmed against each file's `BITMAPINFOHEADER` — three plane offsets relative to absolute 16, and
then a 16-byte table:

```
02 14 26 38 4a 5c 6e 7f 82 94 a6 b8 ca dc ee ff
```

byte-identical in every file that parses. Those sixteen bytes are the whole of the constant-table
content the format carries. The dyad correction tables it needs to reconstruct a picture are not
among them. Decoded luma is uniformly even — 115 distinct values, every one even, across 151 frames —
so the codec works at seven bits of luma, which is itself a fact worth recording.

## TrueMotion 2 is the exception, and is two-thirds recovered

TM2 carries its own Huffman trees and delta tables, so it is self-describing and it *is* implementable
from files alone. It was taken as far as the entropy layer, using ffmpeg's decoded output as a
black-box oracle and never its source.

Verified, with the evidence:

  - **Packet**: 24-byte header; magic `00 00 01 01`; `(size-8)/4` as big-endian 24-bit at +5;
    `size*2` as little-endian 24-bit at +8; **width x 8** at +12 and **height x 4** at +14.
  - **Exactly eight length-prefixed streams** follow, each a 32-bit dword count then payload —
    verified on 2241 of 2242 packets across five files. The one exception is the final packet of a
    file whose RIFF size exceeds its actual length, which is a truncated download rather than a
    format variant.
  - **Each stream** is a count then a chain of blocks, each a 24-bit length in dwords plus a tag byte:
    `0x00` data, `0x40` Huffman table, `0x80` extra, and a word whose top byte is `0xFF` as a
    four-byte marker meaning "reuse the previous delta table". All 13941 streams parse with the chain
    covering them exactly.
  - **Huffman table block**: alphabet size, node count, and a third word whose top five bits give the
    symbol width; tree bits read most-significant-first within little-endian dwords, `0` introducing a
    leaf and its symbol, `1` an internal node. The node count is odd in all 13941 cases, so leaves are
    `(N+1)/2`. **Every one of the 13941 streams decodes exactly**, each consuming its token block to
    within the 0–31 bits of dword padding.
  - **Block type to stream allocation**, solved exactly with zero residual over 2241 frames: type 0
    takes 16 luma and 8 chroma tokens, type 1 takes 16 luma and one chroma pair, type 2 takes 4 luma
    and one chroma pair, types 3 and 5 take none, type 4 takes a 24-value update, type 6 takes two
    motion tokens.
  - **Type 5 means unchanged** — 100% separation against the oracle's changed-block map on every frame
    tested.
  - **Colour**: green is luma, blue is luma plus the first chroma value, red is luma plus the second,
    with chroma constant across each 2x2 quad. RGB-native, so the chroma-siting caveat that applies to
    every 4:2:0 codec here does not arise.
  - **Delta table** is centred and doubling — index 12 is zero, then plus and minus 2, 4, 8, 16, 32,
    64. Chroma uses the same table, with type-0 tokens ordered V then U per quad.
  - **Prediction** is planar: `Y[x,y] = Y[x-1,y] + Y[x,y-1] - Y[x-1,y-1] + delta`, exact across all
    four position classes over 66,080 samples including block edges, for types 0 and 1.
  - **Motion vectors**: one token gives the horizontal component and the next the vertical; symbol 3
    is -2 across 4139 occurrences and symbol 4 is +2 across 5100.

What remains: block types 2 and 3 — 11% of intra blocks — do not follow the planar rule at their top
row and left column, and their residuals there are plus or minus 1 and 3, values absent from the delta
table, which points to an interpolation with rounding rather than a coded delta. The inter path for
types 4 and 6 and the bit packing of the delta-table block are also unfinished.

A decoder handling only types 0 and 1 was deliberately not shipped. A wrong type 3 in a still passage
is indistinguishable from the codec working, which is the failure mode this project has spent a long
time removing from other readers.

# VP6, where the tables are published and the decode still does not come out

VP6 — `VP60`, `VP61` and `VP62` in an AVI, code 4 in Flash Video — is the odd one out here, and it is
worth separating from the five above for exactly that reason. It has a specification, the On2 *VP6
Bitstream & Decoder Specification* version 1.02 of 17 August 2006, mirrored at
`multimedia.cx/mirror/vp6_format.pdf`, and that document prints every constant the format needs:
the baseline mode probabilities and the sixteen pre-agreed mode vectors, the motion vector defaults
and the probabilities their updates are written with, the seventeen bicubic filter sets, both
quantiser tables, the scan order and its band labels, the token set with its extra-bit probabilities,
and the DC, AC and zero-run update probabilities. So the argument that settles Indeo and TrueMotion
does not apply to VP6 at all. Everything is on the page.

Every table was transcribed and then checked back against the document by a script that pulls the
numbers out of the specification's own text and out of the source and compares them element by
element. Thirteen tables were checked and twelve agree exactly; the thirteenth agrees at a one-place
offset that the transcription introduces on purpose, the specification writing `NA` for the DC entry
of the scan-order update probabilities where the source keeps a placeholder. The failure below is not
a mistyped table.

No VP6 encoder exists in ffmpeg, so samples came from `samples.ffmpeg.org/V-codecs/VP6/`.
`predator2_vp60.avi` and `predator2_vp61.avi` are the same 320x240 clip, 299 frames, with a single key
frame at frame 0 — the long-GOP shape this project tests with — and between them they cover both
layouts, one being Simple profile in two partitions and the other Advanced profile in one. ffmpeg
decodes both, so it is a usable oracle. Every measurement below is on the Y, U and V planes and never
on RGB.

## Four things the specification gets wrong, each settled against the bitstream

  - **The BoolCoder's split.** Section 7.3 gives it as `1 + (((Range-1) * Probability) >> 7)`. The
    shift is eight, not seven. At the initial range of 255 and the even-odds probability of 128 the
    stated form yields 255, so a bit written at even odds would decode as zero for all but one value
    of the window; with eight it yields 128 and halves the interval, as an even-odds bit must. The
    community description of the VP5/VP6 range coder gives the threshold as
    `t = 0x100 + (0xff00 & (((high - 0x100) * p) >> 8))`, which with `high = 256 * Range` reduces to
    exactly `1 + (((Range-1) * p) >> 8)` — the same arithmetic, arrived at independently.
  - **The units of the picture size.** Table 2 calls the four size fields VFragments, HFragments and
    their output counterparts, says each counts 8x8 blocks, and illustrates it with "if the image is
    240 pixels high, VFragments will be 30". Every stream measured says otherwise. The fields sit at a
    known offset and read plainly as `0F 14 0F 14` — 15 and 20 — in files ffmpeg decodes at 320x240.
    The unit is the macroblock, sixteen samples, not eight.
  - **The flag in bit 0 of the first byte.** The specification calls it MultiStream. The community
    documentation calls it a marker meaning 0 for VP6.1 and 6.2 and 1 for VP6.0, and that is what the
    samples show: the VP6.0 file sets it and the VP6.1 file does not.
  - **What Buff2Offset counts from.** It is the offset from the first byte of the coded frame, the raw
    header included, not from the end of the header. Measured: with that reading the first partition
    of the key frame consumes 76 to 77 of the 78 bytes it is given, the remainder being the coder's
    flush. The other reading overruns.

## What was verified

  - **The frame header**, both profiles and both partition layouts. The four size fields read 15, 20,
    15, 20 from a 320x240 file. The three-bit tail — two bits of ScalingMode and one of UseHuffman —
    was located by sweeping the number of bits read there from zero to four; only a tail of exactly
    three produces a coherent parse of everything after it, and it does so in both files.
  - **The boolean decoder**, against known ground truth: primed with four bytes and reading eight
    even-odds bits at a time it returns the picture size fields the container independently confirms.
  - **The partition boundary**, as above.
  - **The coefficient probability update section** — 22 DC flags, the scan-order flag, 28 zero-run
    flags and 396 AC flags, in the order of Figure 5. The values it recovers are semantically coherent
    in a way a misparse does not produce: the three "preceded by zero, by one, by more than one" sets
    are monotone in the right direction at every node measured. The probability that a coefficient is
    zero runs 210, 176, 114 across those three contexts, and the probability of end-of-block runs 220,
    188, 124 — that is, after a large coefficient the next one is less likely to be zero and less
    likely to end the block, which is what the statistics of a real picture look like.
  - **Both files recover byte-identical probability tables**, which is the expected result for two
    encodings of one clip and a further check on the parse.

## Where it stops, precisely

At the first block of the first key frame.

That block's true coefficients are known exactly. The forward DCT of ffmpeg's output for it, divided
by the quantisers the frame header states, gives −23, −4, +2, −1, +1, −1 at scan positions 0, 1, 5, 6,
14 and 16 and zero everywhere else, with no value further than 0.03 from an integer — so the quantiser
index, both quantiser tables and the scan order are all confirmed at the same time.

Written out as the sequence of binary decisions that must be read to encode that block, it is 46 long.
**The decoder agrees with the encoder for the first eight of them** — through the DC token's ZERO, ONE,
LOW_VAL and HIGH_LOW nodes and both category nodes, so the token tree, the neighbour context, the
category and the first two magnitude bits are all right — and diverges on the third magnitude bit.

Inverting the decoder makes the size of the gap concrete. Bisecting on the code value for the byte
string that would encode the true block under this model gives one beginning `fe df 5f 34`; the file
has `fe e5 dd 31`. The first byte agrees and the second does not.

The block can be reproduced by changing four of the 46 probabilities, which says the structure is not
the problem — the tree shapes, the band and preceding-value contexts, the zero-run coding, the scan
positions and the DC predictor all survive. But two of the four required values are odd, and a
transmitted VP6 probability is always even, being seven bits doubled. Those two are therefore
compensating for accumulated state rather than naming a table that is wrong, and the repair does not
generalise: extended across the whole first macroblock it needs 34 changes in 278 decisions, including
changes to the sign bit's probability, which is 128 by definition.

Searched and excluded, none of which helps:

  - every starting byte and every bit offset within the whole 4711-byte packet — one coincidence, at a
    position 3092 bytes into the coefficient data, and nothing else;
  - one and two extra decisions consumed before the block, at every probability;
  - all twelve loop orders for the coefficient probability update section;
  - probability fields of six, seven and eight bits — seven, doubled, is confirmed, since six and
    eight each recover 13 updates from the key frame where seven recovers 51;
  - five forms of the range coder's split, and a wider sweep of 432 variants of its arithmetic;
  - the DC probabilities used raw and through the node equations, in all three neighbour contexts and
    both planes;
  - both orders of the magnitude bits against both orders of their probabilities, and the sign read
    before them as well as after.

Nothing was shipped. A VP6 decoder that desynchronises inside the first block does not produce a
picture anybody would mistake for right, but the rule this project works to is the stronger one: a
codec is either exact against the oracle on every plane of every frame or it is not offered, because
the failure that matters is the one that looks like a still passage rather than like noise.

## What is already solved, if this is picked up again

Only the entropy layer is in the way. The reconstruction half of VP6 is the VP3 transform, and that
is now in the tree: `Codecs/Theora/TheoraInverseDct.cs` is the same 8x8 integer transform on the same
seven constants, in its normative form. The On2 document's version of it is incomplete in a way that
was measured here before the Theora work landed — printed with a plain `>> 4` on the column pass, it
comes out one level low almost everywhere against the reference, and adding the rounding term brings
it to a scatter of plus or minus one that the specification's missing 16-bit truncations account for.
Anybody resuming VP6 should take the transform, the reference-frame handling and the border extension
from the Theora decoder rather than from the On2 document, and spend the effort on the first eight
decisions of the first block.

## VP5

Not attempted. VP5 shares the range coder and the coefficient model family with VP6, so it is behind
the same wall, and unlike VP6 it has no published specification at all — only a community description
of its range coder and frame header. Whatever unblocks VP6 is the thing to try first on VP5.

# Lagarith, where the entropy coder's state is a floating-point number

Lagarith (`LAGS`) is a lossless codec of the HuffYUV family: median prediction, a run-length pass over
the zeroes, and then a range coder rather than a Huffman table. It was investigated because lossless
is the sharpest bar this package has — max delta 0 or the decoder is wrong — and because two of its
relatives, Ut Video and MagicYUV, both reached that bar. This one does not, and the reason is
specific rather than a shortage of effort.

Samples were fetched from `samples.ffmpeg.org` under `V-codecs/lagarith/` — `lagarith.avi`,
`lagarith422.avi` and `sample-yv12-lags.avi`, 452 frames between them, covering all three of the
colour arrangements a real file uses. ffmpeg decodes all three. Nothing was committed.

## What was recovered, and verified

The frame layer comes out completely, and it agrees with the codec author's own description on
MultimediaWiki, which he wrote there himself in 2006:

  - **Byte 0 is the frame type.** The published list runs 1 to 11: uncompressed, unaligned RGB24,
    arithmetic-coded YUY2, arithmetic-coded RGB24, solid grey, solid colour, an obsolete RGB
    keyframe, arithmetic-coded RGBA, solid RGBA, arithmetic-coded YV12, and a reduced-resolution
    frame. A frame of no bytes at all means the picture is unchanged from the one before.
  - **Two 32-bit little-endian plane offsets follow it**, and the first plane's data begins at byte 9.
    Verified on every frame of all three files: 451 of 452 have offsets that rise, land inside the
    frame, and leave three pieces whose sizes are in the ratio the pixel format implies. The one that
    does not is the last packet of `sample-yv12-lags.avi`, whose published file ends part way through
    a frame — its checksum matches the manifest, so the sample is truncated rather than the download.
  - **The three files use types 4, 3 and 10**, which is arithmetic-coded RGB24, YUY2 and YV12, and
    their stream descriptions carry 0, 1 and 2 in the four bytes behind the `BITMAPINFOHEADER`.
  - **Each plane opens with one byte that is the run-length escape length** — how many zeroes in a row
    trigger a run, which the author's changelog says is 1, 2 or 3, with 0 meaning the plane was coded
    without the run pass at all. Measured over 1,353 planes: `lagarith.avi` uses 1, 2 and 3 across its
    planes, `lagarith422.avi` uses 3 throughout, and `sample-yv12-lags.avi` uses 2 and 3 with a single
    plane at 0.
  - **Red and blue are carried as their difference from green**, and the prediction is HuffYUV's
    median. Both are stated by the author.

## Where it stops

Everything above is the wrapper. Inside it is a range coder, and four things about that coder are
published nowhere:

  1. how it is initialised — the starting range and the number of bytes that prime it;
  2. its renormalisation rule, of which the only public statement is that the denominator is the top
     two bytes of the range;
  3. how the decoder knows to stop;
  4. the layout of the per-plane probability header, which carries a scale and an escape count.

The community description of the fourth — that the probabilities are Fibonacci-coded with a run
escape for zeroes — was tried against all nine planes of the three files under twelve readings: the
code taken as the value and as a length prefix, bits taken most and least significant first, and with
and without the little-endian word swap that its two nearest relatives need. **No reading produces a
plausible table for more than one plane at a time**, and none produces 256 probabilities summing to
anything round. The published sentence is a sketch by somebody who had read the format, not a
specification, and the wiki still carries its original "describe compressed data layout" note twenty
years on.

## The part that makes this different from a shortage of effort

**The coder's state is a floating-point variable**, and the scaling in the probability header is
computed in floating point and has to round exactly the way the reference implementation's x86
arithmetic rounds. The FFmpeg developers hit this directly while writing their decoder: on one clip a
probability came out as 0x700 where the reference gives 0x6ff, and their conclusion was that ordinary
floats are not portable enough to match it. That is not a detail to be tidied up later. It means the
format is defined by the rounding behaviour of one implementation's floating-point unit rather than by
anything writable down, and reproducing it means reproducing that implementation.

There are exactly two descriptions of this coder in existence, and both are implementations — the
codec's own GPL source and ffmpeg's decoder. This project does not transcribe implementations, so
neither is available, and there is no third thing to read.

**And the oracle is not sound for this bar.** FFmpeg's own issue tracker records its Lagarith decoder
as not bit-exact. Every other codec here was accepted by comparing against ffmpeg's decode of the same
bitstream; for a codec whose standard is exact equality, an oracle that is itself known to be
inexact cannot establish that standard. Even a decoder written here that happened to be right could
not be shown to be right, and one that was wrong would not be caught. That is the second wall, and it
stands whichever way the first one is got round.

## What would change the answer

A published description of the range coder — initialisation, renormalisation, termination and the
probability header's fields — from a source that is not an implementation. Failing that, a reference
decoder that is bit-exact, so that a comparison could mean something. Neither exists today, and the
frame layer above is recorded here so that whoever finds one does not have to start from the
container.
