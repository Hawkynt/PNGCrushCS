# Codecs investigated and not implemented

Nine codecs were investigated and none was implemented. That is a result rather than a gap, and it is
written down here so the work is not repeated by somebody who assumes it was never attempted.

They stop in four different places, and the four are worth keeping apart. Five need constant tables
that are not in the file. VP6 and VP5 stop somewhere else entirely: VP6's tables **are** published,
every one of them was transcribed and checked, and the decode still does not come out. Lagarith stops
at a third place again — its wrapper comes out completely, and the entropy coder inside it is defined
by the rounding behaviour of one implementation's floating-point unit rather than by anything written
down. DV stops at a fourth: its own frame layer is recovered and measured directly against real files,
but its two central tables — the entropy code and the macroblock shuffle — live only in a standard that
is not free to read and, for one of them, in exactly one secondary source this project cannot fully
trust on its own. Where each stops is recorded below.

None of the nine had anything committed.

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

# DV, where the two tables that matter live only in a standard nobody gives away

DV (`dvvideo` — IEC 61834 and SMPTE 314M, what a MiniDV camcorder, a DVCAM deck or a DVCPRO deck all
write) went into `codec-coverage.md`'s "what is left" list with a note that it looked like the cheap
one of the professional formats: intra-only, and "a format with a published standard behind it," unlike
the reverse-engineered game and screen-capture codecs beside it there. That note was wrong, or at least
not the whole truth, and this is the correction.

IEC 61834 and SMPTE 314M are real standards and neither is free. The IEC's own store sells 61834 by the
part; SMPTE sells 314M for about $34 as of libdv's own project page, which is the closest thing to an
official position on the question — the Quasar DV codec (libdv) project states outright that it could
not include the standards because they are not free, and points newcomers at Sony's *DVCAM Format
Overview* brochure instead, calling it a free substitute good enough to understand the source by. That
brochure is the one genuinely independent free document this format is supposed to have, and it could
not be found: the only citation of it left on the web is Adam Wilt's DV technical reference page, itself
pointing at a Sony Canada URL last known good around 2005, long dead, and no mirror of the PDF turned up
anywhere searched.

## What was recovered, and verified against real files

The container layer comes out completely, and every piece of it was cross-checked against at least two
independent sources and then measured against ffmpeg's own encoder:

  - **The DIF block and frame structure.** An 80-byte DIF block — a 3-byte ID and a 77-byte payload —
    is the unit of everything: header, subcode, VAUX, audio and video blocks are the same 80 bytes with
    different contents. A DIF sequence is 150 of them: 1 header, 2 subcode, 3 VAUX, 9 audio and 135
    video, stated identically by RFC 6469 and by the Wikipedia article's own citation trail, and a frame
    is 10 sequences at 525/60 or 12 at 625/50.
  - **The frame-size invariant, measured rather than read.** `ffmpeg -f lavfi -i testsrc=size=720x480 -c:v
    dvvideo -pix_fmt yuv411p -g 1000` for thirty frames wrote a file of exactly 3,600,000 bytes — 120,000
    a frame, exactly 10 × 150 × 80. The same at 720x576 and `yuv420p` for twenty-five frames wrote
    exactly 3,600,000 bytes as well — 144,000 a frame, exactly 12 × 150 × 80. Both are on the nose, no
    slack anywhere.
  - **The chroma trap the task brief called out, reproduced directly.** `ffprobe -fflags +noparse` on
    the two files above states `pix_fmt=yuv411p` for the 720x480 file and `pix_fmt=yuv420p` for the
    720x576 one. Nothing here assumed the answer; both came from encoding real content at each system
    and reading back what ffmpeg's own encoder chose.
  - **The macroblock and superblock arithmetic.** 720x480 is 45×30 macroblocks (1,350), 720x576 is
    45×36 (1,620). Both a Sony/Panasonic transcoding patent's background section (US 6,944,226,
    discussed below) and an independent academic paper on a real-time DV software encoder (Arora, Kant
    and Ramkishor, *Design and Implementation of a Real Time High Quality DV Digital Video Software
    Encoder*, EC-VIP-MC 2003) state that 27 macroblocks make one superblock and that a video segment —
    the unit one DIF video block carries — draws one macroblock from each of five superblocks. 1,350
    and 1,620 both divide by 27 exactly, into 50 and 60 superblocks, and both divide again by 5 into 10
    and 12 — the same 10 and 12 as the DIF sequence counts above, arrived at from an entirely different
    direction. That agreement is real evidence the container-layer numbers are right.

## What was found in exactly one place, and could not be checked against a second

US Patent 6,944,226, *System and associated method for transcoding discrete cosine transform coded
signals* (Lin, Bushmitch, Braun, Mudumbai and Wang; assigned to Matsushita Electric Corporation of
America, granted 2005), is a genuinely independent source — a patent's background description of prior
art, not source code, and not ffmpeg's — and it is the only place either of DV's two central tables
turned up in weeks of searching that reached patents, academic papers, standards-body listings, national
standard mirrors, RFC text, MultimediaWiki, and every DV technical overview page findable.

Its Table 1 is the class/quantisation-number/area-number quantisation step-size table, printed as a
22-row grid where each of the four class columns is a staircase offset from the one before it. Its FIG.
4A shows a superblock's 27 macroblocks numbered 0 to 26 in the order a video segment index draws them,
which is the shuffle table — the exact fact this format's signature failure mode turns on.

Both come with a real reservation and neither was carried into a decoder on the strength of one reading:

  - The patent describes DV50 — 4:2:2, eight DCT blocks a macroblock — not the 4:1:1/4:2:0, six-block
    macroblock this task targets. Nothing in it states that the superblock numbering it shows for DV50
    is the same one DV25 uses; the macroblock/superblock *arithmetic* above is confirmed to be shared
    (27 macroblocks a superblock, 5 across a DIF sequence), but the internal *order* in FIG. 4A is a
    single, unconfirmed data point for the format actually wanted here.
  - Table 1 is a scanned grid with four columns that share the same 22 physical rows at different
    offsets, exactly the layout most prone to transcribing a digit into the wrong cell, and this project
    checks a table like that element by element against a second rendering before trusting it — which is
    what settled VP6's transcription. No second rendering of this table exists anywhere found.

## What was never found anywhere independent

The AC-coefficient run-amplitude entropy code — the table that actually does DV's compression, and the
one piece a working decoder cannot approximate or guess its way past — was not found in any form outside
implementation source. One patent (US 7,681,013, on lookup-table VLC decoding generally) states in
passing that a hardware table for this code holds "64K entries, each entry a triplet of {run, level,
code length}" with codes up to 16 bits, which describes the table's shape and says nothing of its
contents. The DC coefficient's own coding, and the exact byte offsets of a macroblock's own header —
which byte carries the quantisation number, which bits carry each block's class number — were not found
stated anywhere independent either; US 6,944,226 states that a QNO is read once a macroblock and a class
number once a block, and no more.

## Why this is a wall and not a shortage of effort

Everything above came from real searching, not from stopping early: patent literature (Sony's,
Matsushita's and others' filings on DV encoding, transcoding and hardware decoding), the academic
literature on real-time DV encoding, RFC 3189 and RFC 6469, Wikipedia's own citation trail, SGI's
DIVO-DVC option board documentation (`techpubs.jurassic.nl`, Appendix F), Adam Wilt's long-standing DV
technical reference page, the libdv project's own page, and searches of the Bureau of Indian Standards'
and China's national standards mirrors for a free adoption of IEC 61834 that turned out not to exist.
Every one of these describes DV's shape and none of them prints its entropy table or confirms its
shuffle table for the format this task targets.

Shipping a decoder on the strength of the container layer alone — which is solid — plus an entropy
table built by guessing at run-amplitude assignments, or a shuffle table taken from a different chroma
format's patent figure without a second source to check it against, would produce exactly the failure
this format is singled out for above every other codec in this package: a picture with blocks in
roughly plausible places and colours roughly plausible, which is a wrong decode indistinguishable by eye
from a right one. That is worse than refusing, and it is what refusing here is for.

## What would change the answer

A genuinely independent publication of the AC/DC entropy table and the macroblock header layout — a
library or standards mirror carrying IEC 61834 or SMPTE 314M for free, a working copy of Sony's *DVCAM
Format Overview*, or a second patent or paper that reprints the same run-amplitude table US 7,681,013
only describes the shape of. Failing that, US 6,944,226's Table 1 and FIG. 4A checked against a second
independent rendering of the same two tables would at least settle the quantisation and shuffle
question, even without the entropy table. The container-layer facts above — the DIF and frame
arithmetic, both frame sizes measured exactly, both chroma formats confirmed — are recorded here so that
whoever picks this up again starts from the block layer and not from the container.

# Windows Media Video 7 (WMV1), where the tables are MS-MPEG4v3's own

WMV1 — Microsoft's name is Windows Media Video 7 — was investigated because it looked like it might
have an advantage MS-MPEG4v3 did not: ffmpeg carries a real `wmv1` encoder, so a corpus can be built and
driven toward whatever codeword needs to be seen, which is exactly how Microsoft MPEG-4 version 2's
tables were recovered — see that codec's section in `README.md`. That advantage turns out not to apply
here, and the reason is in the one document this whole family is read from.

Michael Niedermayer's *DIVX3 / MS-MPEG4v1-v3 / WMV7-8* — the same GNU Free Documentation Licence
syntax description MS-MPEG4 version 2 was built from — numbers the five bitstreams 1 to 5: MS-MPEG4v1,
v2, v3, WMV7/WMV1 and WMV8/WMV2, and writes every bitstream element's syntax once with a "Version"
column stating which of the five numbers read it. Its own copy at `ffmpeg.org/~michael/msmpeg4.txt` no
longer resolves; the copy read here is the Internet Archive's capture of it,
`web.archive.org/web/20211205015009/http://ffmpeg.org/~michael/msmpeg4.txt`, taken while it was still
live.

## What the document says versions 3, 4 and 5 share

The document is precise about where WMV1 and WMV2 genuinely differ from MS-MPEG4v3, and it says so in
as many words: "MSMPEG4 upto version 3 is pretty much ISO-MPEG4 ... WMV1 just has different scantables
too and WMV2 additionally uses 8x4, 4x8 DCT ... and supports horizontal quarterpel". Two tables are
then given in full because they are exactly what changed — the scan tables (stated as replaced
outright for versions 4-5) and the DC dequantisation scale, printed as two 31-entry arrays that differ
from version 3's own two 31-entry arrays only in their low end. Both are small, both are in the
document, and neither is the problem.

Every large table is not in the document at all, and the version column shows that versions 3, 4 and 5
read the same ones. The macroblock type/coded-block-pattern joint code (`table_mb_intra`) carries the
version marker `345` for I frames without qualification. The run-level table selectors —
`rl chroma_table_index` and `rl table_index`, each a three-value code choosing among what the run-level
coding section calls, throughout, "six run-level tables" once chroma and luma are counted separately —
carry the same `345` marker, as does `dc_table_index` and, in the P-frame branch, `mv_table_index`.
Nowhere in the thirty-one-entry dequantisation tables' neighbourhood, or anywhere else in the document,
is there a second run-level, DC or motion-vector table given for version 4 or 5 the way the scan tables
and the dequantisation scale were. The document's own reference for all of them is one line: a link to
`msmpeg4data.h`, an implementation, for every version at once.

Two numbers in the syntax pin this down harder than the missing tables alone would. The DC bitstream
for versions 3-5 reads `luma_dc_vlc[dc_table_index]` or `chroma_dc_vlc[dc_table_index]`, and "if
level==119" is the escape that reads a raw byte instead of trusting the small code — **119**, not a
round number, identical for versions 3, 4 and 5 in the one place the document states it. The
motion-vector bitstream for the same three versions reads `mv_vlc[mv_table_index]`, and "if code==1099"
is its escape into six raw bits each way — **1099**, again identical across all three, and again the
exact figure MS-MPEG4v3's own investigation already named: "the motion vector tables pair one code with
a whole vector across some eleven hundred entries." A format that had actually redesigned these tables
for WMV1 and WMV2 — the way it demonstrably redesigned the scan tables and the DC scale — would have no
reason to land on the same three-digit sentinel value in two unrelated tables by coincidence. The
document does not say the tables are identical between versions 3, 4 and 5 in a single sentence; it
says so by giving one syntax, one set of escape constants and one external reference for all three, and
by being exactly the kind of document that flags a difference in the open when there is one.

## Why an encoder does not rescue it

WMV1 has what MS-MPEG4v3 did not: `ffmpeg -c:v wmv1` writes real files, so `-idct faani`-grade encoder
control is available here in a way it never was for version 3. It does not help, because of where the
missing tables sit in the bitstream rather than because nothing can be encoded.

A macroblock in an inter picture reads its joint type/CBP code, then its motion vector, and only after
that does it read the coded blocks — so the motion-vector codeword for the *first* macroblock of a
frame sits at a fixed, locatable position once the type/CBP code in front of it is known. Every
macroblock after the first does not: reaching it means having already decoded every earlier
macroblock's coded blocks in full, because a run-level code's length is not known until it is decoded,
and that decoding needs the very six tables in question. There is no way to skip ahead. A corpus can be
built as large as disk space allows and it changes nothing about this: what it buys is one motion-vector
codeword and one type/CBP codeword per independently-reachable position (the first macroblock of each
slice) and nothing at all from any macroblock after it, in any frame, ever — the six run-level tables
stay at zero coverage regardless of corpus size, because the mechanism that would read a second
macroblock's codeword is the same mechanism the corpus is trying to recover.

Even granting the most generous reading — that the joint type/CBP table alone is small enough to
recover the way MS-MPEG4v2's two macroblock tables were, and that this unlocks the first
macroblock of every slice in every frame of an arbitrarily large corpus — the motion-vector table's own
scale defeats it. Driving an encoder to choose one specific vector value, at one specific probability
bucket, for the one macroblock in the whole frame that happens to sit first, is a considerably harder
target than simply encoding varied motion; MS-MPEG4v3's own investigation reached exactly this
conclusion for the same ~1,100-entry table under weaker constraints than "and it has to be the first
macroblock too," and nothing about WMV1 changes what the table itself asks for.

## What was verified

`ffmpeg -h encoder=wmv1` confirms the encoder exists and writes `yuv420p`. The document's small tables —
the scan tables, the DC dequantisation scale, the `c3` code (`0`→`0`, `10`→`1`, `11`→`2`) every
three-valued selector in the header uses — are given in full and are not in question; what is in
question is only the run-level, DC and motion-vector code tables, and those are absent from the
document for every version alike, tied to version 3's own already-established figures by two shared
escape constants rather than merely by family resemblance.

## What would change the answer

The same thing that would change MS-MPEG4v3's answer: a publication of the six run-level tables, the
two DC tables and the two motion-vector tables that is not somebody's implementation. Barring that, a
demonstration that WMV1's tables are genuinely smaller or differently shaped than version 3's — which
the shared escape constants above argue against, but do not by themselves rule out beyond doubt.
