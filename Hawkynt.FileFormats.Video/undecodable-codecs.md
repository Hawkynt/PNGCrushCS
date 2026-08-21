# Codecs investigated and not implemented

Twenty-four codecs were investigated and none was implemented. That is a result rather than a gap, and
it is written down here so the work is not repeated by somebody who assumes it was never attempted.

They stop in four different places, and the four are worth keeping apart. Five need constant tables
that are not in the file, and WMV1 and WMV2 join them on the same evidence, tied to MS-MPEG4v3's own
already-missing tables by shared escape constants rather than by file size. VP6 and VP5 stop somewhere
else entirely: VP6's tables **are** published, every one of them was transcribed and checked, and the
decode still does not come out. Lagarith stops at a third place again — its wrapper comes out
completely, and the entropy coder inside it is defined by the rounding behaviour of one implementation's
floating-point unit rather than by anything written down. DV stops at a fourth: its own frame layer is
recovered and measured directly against real files, but its two central tables — the entropy code and
the macroblock shuffle — live only in a standard that is not free to read and, for one of them, in
exactly one secondary source this project cannot fully trust on its own. MSS1, MSS2, Canopus HQ, HQA
and HQX, and MSZH are a variant of the first place rather than a fifth: their tables or their coder are
undocumented too, but the evidence is a detailed write-up that turns out to be somebody else's
reverse-engineering notes, or no description at all, rather than a file too small to hold what is
needed. Escape 124 and SpeedHQ stop closer to the finish than any of the rest: each has its container
and most of its bitstream recovered and verified against real files, with one specific, narrow piece —
a skip-count coding for Escape 124, some unknown number of reassigned coefficient codewords for SpeedHQ
— that this project's evidence could narrow down but not close. Where each stops is recorded below.

The nine screen-capture codecs below — MSCC, RSCC, WCMV, MWSC, RASC, Go2Meeting, ScreenPressor,
Screenpresso and TSCC2 — sort into the same places rather than needing a new one. Six of the nine
(MSCC, WCMV, MWSC, RASC, ScreenPressor and Screenpresso) join MSS1, MSS2, Canopus and MSZH's variant of
the first place: the only bitstream-level description that exists for any of them, where one exists at
all, is a paraphrase of the implementation that produced it, and for three of the six no usable sample
corpus exists either — a wall MSS1 and MSS2 did not have to clear, since ffmpeg still carries no
`mscc`, `wcmv`, `rasc` or `screenpresso` encoder and no third-party sample archive searched carries a
file for any of those four. TSCC2 and Go2Meeting are the same variant on better-attested evidence: each
has one detailed technical write-up, and each write-up's own author is on record as the implementation's
author too. RSCC alone reaches Escape 124 and SpeedHQ's place — a real sample corpus, a container and a
packet framing fully recovered and verified against it, and a delta-record scheme whose destination
coordinates are pinned down and whose remaining fields resist every reading tried.

None of the twenty-four had anything committed.

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
that does it read the coded blocks — so a macroblock's motion-vector codeword sits at a fixed, locatable
position once the type/CBP code in front of it is known, without needing the run-level tables at all.
That is true of more than just the first macroblock of a slice: a skipped macroblock carries no bits
beyond its skip flag, and an unskipped one whose CBP comes out to zero calls `block()` six times but
reads nothing from any of them, so either one hands the next macroblock's header straight over without
ever touching a run-level table. What resets synchronisation is any *coded* block — the moment CBP is
non-zero for some block, its run-level codewords have to be decoded to know how many bits they consumed,
and that is the one thing not available; every macroblock from there until the next skip or all-zero
macroblock is unreachable, not merely unread.

This still does not open a route to the six run-level tables themselves, and the reason is not about
how many macroblocks are reachable but about what a reachable one can prove. Every macroblock whose
start position is known this way is, by construction, one that has no coded run-level codeword to
observe — that is what makes it reachable. A corpus therefore never delivers a codeword that starts at
a known bit offset *and* is guaranteed non-empty, and without both, there is nothing to test a candidate
table against: any bit sequence that follows a reachable macroblock could equally well be an unrelated
neighbour's skip flag or the header of whatever comes next. Reaching a specific run-level codeword needs
the position *after* it as well as before it, and that position is exactly what decoding the codeword
was supposed to establish.

The motion-vector table fares better in principle, since its codeword is read before the still-unknown
blocks and its own end position does not depend on them — but recovering it exactly this way is the
same reconstruction MS-MPEG4v3's own investigation already attempted for the identical ~1,100-entry
table and did not complete, because a specific rare vector value has to be driven onto a macroblock
whose start position is independently known, and the two constraints compound rather than add. Nothing
about WMV1 relaxes either one: the table is either version 3's own, per the escape-constant evidence
above, or a WMV1-specific table of the same size never published anywhere, and either way this
investigation did not attempt to redo that reconstruction from scratch.

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

# Windows Media Video 8 (WMV2), which adds a private table on top of WMV1's wall

WMV2 — Windows Media Video 8 — was investigated alongside WMV1 for the same reason: ffmpeg carries a
real `wmv2` encoder, which is more than MS-MPEG4v3's own investigation had to work with. It stops at the
same wall WMV1 does, for the same evidence, and it adds one wall of its own on top.

The source is the same document as WMV1's: Michael Niedermayer's *DIVX3 / MS-MPEG4v1-v3 / WMV7-8*,
numbering WMV2 version 5 of five bitstreams that share one syntax description. Its own copy at
`ffmpeg.org/~michael/msmpeg4.txt` no longer resolves; the Internet Archive's capture of it,
`web.archive.org/web/20211205015009/http://ffmpeg.org/~michael/msmpeg4.txt`, is what this and WMV1's
section were both read from.

## What WMV2 shares with MS-MPEG4v3 and WMV1

The document's version column marks the run-level table selectors, `dc_table_index` and
`mv_table_index` as version `345` throughout — version 5 reads exactly the same fields, in the same
positions, as versions 3 and 4 do. The two escape constants that tie WMV1 to version 3 tie WMV2 to it
as well: the DC bitstream's escape at level **119** and the motion-vector bitstream's escape at code
**1099** both carry the `345` marker without qualification, meaning WMV2 reads the identical sentinel
values version 3 does in the identical fields. Where WMV2 genuinely changes the coding, the document
says so in the open, exactly as it does for the scan tables: WMV2 adds 8x4 and 4x8 sub-block splits
under an adaptive block transform, and it adds horizontal quarter-pel motion compensation with its own
one-bit shift. Both are new syntax on top of the shared frame, not a replacement for the six run-level,
two DC or two motion-vector tables underneath it — the sub-block split changes how many transform
blocks a coded macroblock contains, and the quarter-pel bit changes how a decoded half-pel vector is
refined, but the codewords selecting a run-level table, a DC table or a motion vector still read through
the same `345` fields and the same two escape constants as WMV1's.

## What WMV2 adds that WMV1 does not have

WMV2's P-frame macroblock header reads its joint type/coded-block-pattern code through
`wmv2_inter_table[cbp_index]`, a table selected by `cbp_index` — itself a three-way reordering of a
two-bit code, keyed to the picture's quantiser scale — where versions 3 and 4 read `table_mb_non_intra`
in that position instead. Different variable, different version marker (`5` alone, not `345`): this is
a table of WMV2's own, not one it shares with the version 3 wall this section otherwise leans on. It
sits in exactly the same locked position the joint type/CBP code sits in for every version — first in
every macroblock, gating where every field after it starts — so recovering it faces the same structural
problem as the joint tables discussed for WMV1: it is reachable only for the first macroblock of a
slice, because reaching the second means first decoding the coded blocks of the one before it, and that
decoding needs the six run-level tables that are the actual wall. WMV2 does not inherit a smaller
version of MS-MPEG4v3's problem; it inherits the whole of it and adds a fourth private table — three,
counting one per `cbp_index` value — that WMV1 does not have at all.

One small table is the exception, and it is exactly the kind of thing this project can and does use:
`table_inter_intra`, the four-entry mapping from a coded prediction direction to a luma/chroma pair
`{0,1}, {2,2}, {6,3}, {7,3}`, is printed in the syntax document in full. It governs the rarely-taken
`inter_intra_pred` path, not the run-level, DC or motion-vector tables, so having it does not open a way
through the wall — it is recorded here because it is one of the few tables this codec's syntax document
actually gives, alongside the scan tables and the DC dequantisation scale, and finding it does not
change where WMV2 stops.

## What was verified

`ffmpeg -h encoder=wmv2` confirms the encoder exists and writes `yuv420p`. Every field this section
relies on — the `345` version markers on the shared table selectors, the `119` and `1099` escape
constants, and the `5`-only marker on `wmv2_inter_table` that sets it apart from `table_mb_non_intra` —
is read directly from the syntax document's own bitstream tables rather than inferred.

## What would change the answer

The same publication that would change WMV1's and MS-MPEG4v3's: the six run-level tables, the two DC
tables and the two motion-vector tables, from a source that is not an implementation. WMV2 additionally
needs `wmv2_inter_table`'s three entries published on top, since that table is its own and not covered
by anything that would settle the other three versions.

# Microsoft Screen Codec 1 (MSS1), where the only detailed write-up is somebody else's decoder

MSS1 — Windows Media Screen Codec version 7, FourCC `MSS1` — was investigated as the simpler of the two
screen codecs Microsoft shipped alongside Windows Media Video. It is a real arithmetic coder over a
recursively subdivided picture, not a table-driven codec like the Windows Media Video family above, so
the argument that settles Indeo and TrueMotion does not apply to it directly. It stops one step earlier
than that: there is nothing independent to build from at all.

## What Microsoft published, and what it covers

Microsoft's own documentation of this codec is on `learn.microsoft.com`, under "Windows Media Video 9
Screen Codec", "Windows Media Video 9 Screen Decoder" and "Windows Media Video 9 Screen Encoder". Every
one of those pages is a DirectX Media Object and Media Foundation Transform reference: the class
identifiers (`CLSID_CMSSCEncMediaObject2` to encode, `CLSID_CMSSCDecMediaObject` to decode both MSS1 and
MSS2), the FourCCs, and the pixel formats a decoder instance will hand back — `RGB24`, `RGB32`,
`ARGB32`, `RGB565`, `RGB555`, `RGB8`. That is the same shape the Open Specifications programme's coverage
of the whole Windows Media family already has: the API surface and the container are documented, the
bitstream inside the packets is not. Nothing on any of those pages states a header layout, an entropy
coder, a block type or a context model.

## The one detailed write-up found, and why it cannot be used

MultimediaWiki's "Microsoft Screen Codec" page is the only place a bitstream-level description of MSS1
exists at all: a header layout, an arithmetic coder said to resemble a 1987 paper by Witten, Neal and
Cleary, a context modeller with a last-decoded-pixel cache and several adaptive layers, and pseudocode
for two functions it names `recursive_decode_intra` and `recursive_decode_inter`. Nothing on the page
states where this description came from — no citation to a Microsoft document, no note that it was
reverse-engineered from the codec binary, nothing.

What the page does carry is a strong, specific signal about where it came from anyway. Its function
names for MSS2's own arithmetic coder — `ac2_init`, `ac2_renorm`, `ac2_get_scaled_value`,
`ac2_rescale_interval`, `ac2_get_number`, `ac2_get_prob`, `ac2_get_consumed_byes` — line up one for one
with the functions FFmpeg's own `libavcodec/mss2.c` is independently known to define: `arith2_normalise`,
`arith2_get_scaled_value`, `arith2_rescale_interval`, `arith2_get_number`, `arith2_get_prob`,
`arith2_get_consumed_bytes`. That is not a family resemblance between two people who solved the same
problem; a `get_consumed_byes` typo surviving next to six other functions that keep the same argument
order and the same names in the same sequence, one letter and one prefix apart, is what a page
describing an implementation, function by function, looks like. This project does not transcribe or
translate ffmpeg's source, and a wiki page that is itself a paraphrase of that source is the same
material at one remove — using it to write a decoder here would be exactly what the rule against
transcription exists to prevent, whether or not a single line of code is copied.

Set the wiki page aside and nothing independent is left. The Witten, Neal and Cleary paper the arithmetic
coder is said to resemble is genuinely independent and genuinely published, but it describes arithmetic
coding in general, not this codec: it has no header layout, no block types, no cache sizes, no context
selection rules and no escape thresholds, all of which are Microsoft's own design choices for this
specific format and are exactly what a decoder needs.

## Why blind reverse engineering does not reach it either

Every codec on this page that was mapped from the bitstream alone — TrueMotion 2, Indeo 3's header, the
container layers of DV and Lagarith — had some independently-sourced anchor to start from: a field whose
meaning could be cross-checked against a `BITMAPINFOHEADER`, or a container invariant measured directly
against real files. MSS1 offers no such anchor. Its extradata and its packet bytes are opaque without
already knowing the arithmetic coder's renormalisation rule and the context models it starts from, and
unlike Lagarith — whose author published the frame layer himself in 2006, leaving only the range coder
unresolved — no comparable first-party description of MSS1 exists to start from at all. And unlike every
codec in the "Windows Media before VC-1" family above, ffmpeg carries no `mss1` encoder — `ffmpeg -h
encoder=mss1` reports none available — so a corpus is limited to whatever samples exist already, at
`samples.ffmpeg.org/V-codecs/MSS1/`, rather than one built to order.

## What would change the answer

A description of MSS1's bitstream from a source that is not an implementation: a Microsoft document
that states the header layout, the coder and the context models rather than only the DMO/MFT class
identifiers and pixel formats, or an independent reverse-engineering write-up that says, plainly, how it
was produced and from what.

# Microsoft Screen Codec 2 (MSS2), which adds an embedded codec to MSS1's wall

MSS2 — Windows Media Screen Codec version 9, FourCC `MSS2` — was investigated alongside MSS1. It stops
for the same reason MSS1 does, with one further complication that makes it strictly harder rather than
easier: part of its picture is coded by embedding Windows Media Video 9 image blocks, so even a decoder
that somehow reached the screen-content half would still need the arithmetic-coded half to know where
that other codec's data starts and ends.

## The same absence of a source

Microsoft's own `learn.microsoft.com` pages for the "Windows Media Video 9 Screen Codec" name MSS2 as
the FourCC for the format's version 9 content and describe the same DirectX Media Object and Media
Foundation Transform surface MSS1's section above found: `CLSID_CMSSCEncMediaObject2` to encode,
`CLSID_CMSSCDecMediaObject` to decode — the same decoder class serves MSS1 and MSS2 alike — and the
output pixel formats a decoded frame can be delivered in. No bitstream field, block type or coder rule
is stated anywhere on those pages.

The one detailed technical write-up covering MSS2, on the same MultimediaWiki page MSS1's section
examined, carries the identical problem for its MSS2-specific material: a second arithmetic coder
described with functions named `ac2_get_scaled_value`, `ac2_rescale_interval`, `ac2_get_number`,
`ac2_get_prob` and `ac2_get_consumed_byes`, matching `libavcodec/mss2.c`'s own `arith2_get_scaled_value`,
`arith2_rescale_interval`, `arith2_get_number`, `arith2_get_prob` and `arith2_get_consumed_bytes` — the
same typo included, one letter and one prefix apart across all five. Whatever independent knowledge of
MSS2 the page might also carry is not separable from what reads as a paraphrase of the implementation,
and this project does not use either.

## What the embedding adds, on top of an already-missing coder

MSS2 frames can carry rectangles coded as Windows Media Video 9 — the codec this package already reads
as `WMV3`/`WMV9` intra pictures — inside the same arithmetic-coded container MSS1 uses for its own
recursive subdivision. Even granting, for the sake of argument, that MSS1's coder and context models
were somehow recovered, a working MSS2 decoder would still need to know, from the same undocumented
bitstream, which rectangles are screen content and which are WMV9 image data, and where in the packet
each WMV9 sub-bitstream begins — a fact this project's own VC-1 decoder cannot supply, since it expects
a complete WMV3 elementary stream and MSS2 is not one. That boundary information is exactly the kind of
framing decision a container's demuxer would ordinarily carry, and here it does not exist independently
of the same coder this section has already found no legitimate description of.

## What was verified

`ffmpeg -h encoder=mss2` reports no encoder, the same as MSS1, so there is no way to drive a corpus
toward a specific codeword here either; what samples exist are fixed, at
`samples.ffmpeg.org/V-codecs/MSS2/`. The function-name correspondence between the MultimediaWiki page
and `libavcodec/mss2.c` was checked against both the page's own listed function names and the file names
and function names independently reported for that source file, not assumed.

## What would change the answer

The same thing MSS1 needs: a description of the arithmetic coder and its context models from a source
that is not an implementation, plus, for MSS2 specifically, a stated rule for where a WMV9-coded
rectangle's bytes begin and end inside the packet.

# MSZH, the LCL sibling whose "homebrew LZ77" has no description anywhere

MSZH — the other half of the Lossless Codec Library, FourCC `MSZH` — was investigated straight after its
sibling ZLIB, which this package does decode; see `README.md`. The two share one container down to the
byte: the same eight-byte trailer behind a standard `BITMAPINFOHEADER`, the same colour-space and flag
encoding, the same "reset every frame" framing. Where ZLIB hands its picture to a compressor the zlib
documentation describes in full, MSZH hands it to one Kenji Oshima wrote himself, and nothing describing
that step was found published anywhere.

## What was recovered, and verified against real files

The container layer needed no separate work at all — it is `LclHeader`, already read for ZLIB — and a
sweep of samples.ffmpeg.org's `V-codecs/mszh-zlib/mszh/` directory, sixteen files built as a deliberate
feature test rather than a recording, confirms it reads MSZH correctly too: every combination of the six
imagetypes the format defines (RGB24 and five YUV subsamplings), the multithread flag, and the null-frame
flag are all present, and every field this project's `LclHeader.Read` already parses came out exactly as
the filenames promise.

MSZH's `compression` byte states two things, "0: compression" and "1: no compression", and the second of
them is now fully verified rather than merely read. A packet with `compression = 1` carries the raw
picture with zero framing overhead — measured directly against the packet's own bytes, no oracle needed:
`mszh_rgb24_nocomp.avi`'s single packet is exactly 253,440 bytes, 352 × 240 × 3, and reversing the same
bottom-up flip this package already applies for ZLIB's RGB24 reproduces ffmpeg's own decode of the same
file exactly. The same check holds for all five YUV "no compression" files — `mszh_yuv111_nocomp.avi`
(253,440 bytes, 4:4:4), `mszh_yuv211_nocomp.avi` and `mszh_yuv422_nocomp.avi` (168,960 bytes each, 4:2:2
under two different imagetype codes), `mszh_yuv411_nocomp.avi` and `mszh_yuv420_nocomp.avi` (126,720
bytes each, 4:1:1 and 4:2:0) — every packet's length is exactly its picture's uncompressed byte count and
nothing more, confirming "no compression" means precisely what it says for every colour space the format
defines, not only the one ZLIB was measured against.

## Where it stops

Every other file in that same directory — the ones without `_nocomp` in their name — uses
`compression = 0`, real MSZH compression, and that is where this stops. The format's own document,
`multimedia.cx/lcl.txt`, gives it one sentence: "Mszh compression: works by copying blocks from already
decoded data," immediately followed by its own unfilled placeholder, `[add mszh decompression
algorithm]` — the author's own acknowledgement that this step was never written down, even by him. The
one secondary source found, Kostya Shishkov's public survey of lossless video codecs
(`codecs.multimedia.cx`), categorises LCL in one clause — "left prediction plus deflate or homebrew LZ77
scheme" — without describing either half further.

`mszh_yuv420.avi` and `mszh_yuv420_nocomp.avi` decode, through ffmpeg, to the identical `yuv420p` frame —
confirmed byte for byte — which makes the pair a real ground truth to reverse the compression against:
whatever `mszh_yuv420.avi`'s 108,571-byte packet decompresses to has to equal `mszh_yuv420_nocomp.avi`'s
126,720 raw bytes exactly. Walking the two together finds a single, clean, repeating shape for most of
the stream: one `0x00` byte in the compressed packet that is not present in the raw output, followed by a
run of literal bytes that then matches the raw stream exactly until the next such byte. Those literal
runs are not a fixed length — 45, 19, 30, 48, 18, 27, 37, 48, 14, 50 and sixteen more measured this way,
from as few as 1 to as many as 67 bytes — and nothing about the position, the byte before it or the byte
after it predicts where the next one falls, so there is no length field here to have missed: whatever
determines a literal run's end is not encoded anywhere near its start.

The same walk breaks down completely at raw offset 2,048, and it breaks down instructively. `raw[2048:]`
is a genuine repeat of `raw[56:]` — content that occurred exactly 1,992 bytes earlier — which is what
"copying blocks from already decoded data" describes exactly. But the compressed bytes standing in for
that copy, `f2c817d417e017ec0702`, do not resolve to any offset-and-length encoding found by inspection:
not a little- or big-endian 16- or 32-bit pair at that distance, not a length-prefixed or nibble-split
form tried against it. The same shape recurs in `mszh_yuv111.avi` against its own `_nocomp` sibling — a
clean run of single-byte-marked literals for the first 861 bytes, then a break at a point where
`raw[861:]` is a genuine, verified repeat of a three-byte motif recurring 240 bytes earlier — with the
same result: a real match exists to be encoded, and what encodes it in the compressed bytes was not
recovered.

Two things keep this from being a shortage of effort rather than a wall. First, the corpus is a single
still picture re-encoded six ways, not a recording — every file above is one frame at 352×240 — so there
is exactly one real match token's worth of evidence to calibrate an entirely unpublished encoding against
per colour space, where every codec in this package that was recovered from bitstream measurement alone
needed dozens to thousands of streams to pin a table or a rule down with confidence: Ut Video's slice
division and MagicYUV's permutation were each settled against hundreds of frames, and TrueMotion 2's
Huffman tables against 13,941 independently-decoding streams. Two candidate match tokens, both unsolved,
is not that. Second, ffmpeg carries no `mszh` encoder — `ffmpeg -h encoder=mszh` reports none — so there
is no way to drive a corpus toward a specific codeword the way ZLIB's own encoder let that codec's row
padding be settled directly; what samples exist is what samples exist.

One more thing is worth being precise about, because Lagarith's entry above turns on it: this is not
established as an unsound oracle. ffmpeg's `mszh` decoder was checked against real bytes it did not
produce — the `_nocomp` packets, compared to the file's own raw content directly, not to ffmpeg's opinion
of it — and it was correct on every one of the six imagetypes measured that way. Nothing here tests
ffmpeg's decode of the `compression = 0` path independently, so unlike Lagarith this format was not
reached rather than found unreliable; the distinction is DV's, drawn the same way there.

## What would change the answer

A description of MSZH's compression scheme from a source that is not an implementation — the algorithm
`lcl.txt` itself never filled in. Failing that, several more genuinely distinct compressed recordings,
not further encodings of the one photograph this corpus re-uses six times, would let a reconstruction be
checked against a second independent match token the way every other bitstream-recovered codec in this
package was.

# Escape 124, where the container is solved and the coefficient that matters is not

Escape 124 — Eidos Technologies' vector-quantised codec — was investigated as one of the
smaller game and FMV formats this package's coverage still lacks. It stops with the container fully
mapped and verified against real files, and the codec's own bitstream reconstructed as far as the
published pseudocode goes before running out: a skip-count coding neither that pseudocode nor anything
else found states precisely enough to reproduce.

## The container is not AVI, and is fully solved

Escape 124 is not carried in AVI the way Cinepak or Microsoft Video 1 are. It is usually encapsulated in
ARMovie/RPL files (`.rpl`) — the format early PC Tomb Raider games use — a text header followed by a
binary chunk catalogue, with no relation to RIFF at all. Assuming AVI here would cost the next attempt
real time before it ever reached the codec, so it is worth stating plainly: a working decoder for this
codec needs a new container reader, not a new entry in an existing one.

The header is twenty-one newline-terminated text fields in a fixed order — signature (`ARMovie`), movie
name, copyright, an author/tool line, then decimal value/description pairs for video compression format,
X and Y resolution, pixel depth, frame rate, sound compression format, sample rate, channel count, sample
precision, frames per chunk, number of chunks, even and odd chunk sizes, chunk catalogue offset, sprite
offset and size, and key frame offset. Two real samples from `samples.ffmpeg.org/game-formats/rpl/escape124/`
— `ESCAPE.RPL`, 320x240, and `PYRAMID.RPL`, 320x120 — were read field by field against this layout and
matched exactly, including the video format field itself: both state `124` in plain decimal, confirming
the codec's own name is the format code and not a coincidence of numbering.

What follows the header is a flat binary chunk catalogue at the offset the header states, one line per
chunk, `FO,BS;OS` — file offset, video byte size, sound byte size — comma before the video size and
semicolon before the sound size, each chunk's video and sound data sitting contiguously at its own file
offset. The header's own "number of chunks" field undercounts by one: it names the highest chunk index
rather than a count, so a file stating `3` has four chunks, indices 0 through 3, and the catalogue has
one line per chunk rather than one more than the count. This was found because `ESCAPE.RPL`'s header
states three chunks and twenty-five frames per chunk — seventy-five frames by a literal reading — while
its catalogue holds four lines and ffmpeg reports exactly one hundred frames for the file, `4 x 25`.
Every catalogue entry's file offset plus its two sizes lands exactly on the next entry's file offset in
both samples, which is what closes the loop: the catalogue is internally consistent and its frame count
agrees with ffmpeg's own count, so this half of the format is not a guess.

Inside a chunk's video bytes, frames are packed back to back with no gap, each opening with its own
eight-byte header — a `frame_flags` doubleword and a `frame_size` doubleword stating the whole frame's
size, header included. Walking `ESCAPE.RPL`'s first chunk by `frame_size` alone lands on exactly
twenty-five frame headers and ends precisely at the chunk's own video byte count, with nothing left
over — the same closed-loop check the chunk catalogue passed. `frame_flags` was read the same way: the
first frame of every chunk sequence carries three extra bits (17, 18 and 19) that every following frame
in the sample lacks, and those three bits are exactly the ones a community pseudocode description
(below) names as "unpack codebook 1", "unpack codebook 2" and "unpack codebook 3" — which is what a
keyframe needs and a delta frame does not, and confirms the bit numbering by behaviour rather than by
trusting the source that named it.

## What is confirmed about the codec itself, and how

The only bitstream-level description found is MultimediaWiki's Escape 124 page, itself explicit that it
is incomplete: no bit pattern for the skip-count coding it names "Rice decoding", no loop-termination
rule for the per-superblock macroblock loop, and no statement of a decoder's initial state. What it does
give — a codebook structure, a superblock/macroblock assembly outline, and the frame-header bit numbers
above — was checked against real frame data rather than trusted outright, and two things came out
confirmed:

**The bitstream is read most-significant-bit first.** Every frame opens with a four-bit codebook-1
depth field, and codebook 1's stated size is `2^depth` entries of a known width (a four-bit pixel mask
plus two 5-5-5 RGB colours, thirty-four bits an entry). Reading that four-bit field most-significant-bit
first on `ESCAPE.RPL`'s first frame gives a depth of 9 — a 512-entry codebook, 17,408 bits, comfortably
inside the frame's 99,648-bit payload. Reading it least-significant-bit first gives a depth of 14 — a
16,384-entry codebook needing more than five times the whole payload before a single macroblock is
decoded. A codebook that cannot fit in the frame that supposedly carries it is not a candidate reading,
which is what makes this check work without yet knowing anything else about the format: only one bit
order produces a codebook size the frame could possibly hold, on the very first field read.

**Codebook 1's size is exactly `2^depth`, as stated.** Unpacking 512 entries at thirty-four bits apiece
from that starting position lands the read position at exactly 17,412 bits — 4 bits for the depth field
plus 512 × 34 — with no drift, which is what let the next field (codebook 2's own depth) be read from a
known-correct position rather than a guessed one.

## What is suggestive and not corroborated further

Two more things fit one clean observation each, and are recorded as exactly that — a single data point,
not an established fact the way the two above are:

- **Byte-alignment between codebook sections.** Rounding the read position up to the next byte boundary
  after codebook 1, and again after codebook 2, before reading codebook 3's size, produces a size of
  zero on `ESCAPE.RPL`'s first frame — a clean, structurally sensible answer (a 20-bit field landing on
  exactly zero by chance is roughly a one-in-a-million read) where every unaligned reading tried instead
  produces a codebook 3 many times larger than the space remaining in the frame. One clean zero across
  one field on one frame is a real signal and not a coincidence dismissed lightly, but it is one data
  point, not a rule checked against a second frame or a second file.
- **Codebook 2's size multiplier.** The wiki page states codebook 2 holds `2^depth` entries multiplied
  by "total number of superblocks" — for a 320x240 picture at 8x8 superblocks, 1,200 — which produces a
  codebook many times larger than the space remaining in the frame regardless of which byte-alignment
  reading is used. Multiplying by the superblock count of one row (40, for this picture) instead is the
  only multiplier tried that leaves room for what has to follow it, but nothing beyond that plausibility
  argument was found to confirm it, and no smaller test than decoding a whole frame was available to
  check it in isolation.

## Where it stops, precisely

At the per-superblock skip-count decode, immediately after the third codebook. Every reading of the
"Rice decoding" the wiki page names but does not specify — including the most common shape such a name
suggests, a unary prefix counting leading zero bits followed by that many literal bits, value equal to
the literal plus `2^prefix - 1` — produces skip counts in the hundreds on the first several superblocks
of a key frame, where a key frame, coding every superblock fresh, should skip nothing or next to
nothing. A three-hundred-and-some-superblock skip within the first handful of reads is not a plausible
encoding of "nothing to skip yet"; it is the signature of a bitstream position that is not where the
skip-count coding actually begins, or a coding rule that is not the one being tried. Both remain open:
whether one more alignment step precedes the loop, whether the loop's own structure differs from the
pseudocode's outline in some way not yet tested, or whether the skip code is not the unary-prefix shape
guessed at, cannot be told apart from the evidence gathered so far.

## What would change the answer

A description of the skip-count bit pattern from a source that states it rather than names it — the
exact prefix-to-value mapping "Rice decoding" is standing in for — would very likely settle the rest in
short order, since everything after it (the macroblock mask assembly, the codebook-index update, the
raster order of a superblock's sixteen macroblock positions) is stated in enough detail to implement
directly once the bitstream position feeding it is trustworthy. Failing that, a second and third real
sample decoded far enough to cross-check the byte-alignment and codebook-2-multiplier findings above
against more than one data point apiece would narrow the search considerably, even without a published
skip-count rule to confirm against.

# NewTek SpeedHQ, an MPEG-2-shaped codec that agrees with the real standard almost everywhere and not quite everywhere

SpeedHQ (`SHQ0` through `SHQ5`, `SHQ7`, `SHQ9`) was investigated as the next professional/intermediate
codec after Hap, on the strength of MultimediaWiki's own note for it — unlike Canopus's pages above,
this one states plainly that "NewTek has provided samples and support in understanding the format,"
and ffmpeg carries a real encoder as well as a decoder, so a corpus could be built to order rather than
found. It stops closer to working than any other entry in this file: the container, the field and slice
framing, and the DC coefficient layer all check out exactly against real ISO/IEC 13818-2 tables this
package already carries from its own MPEG-2 decoder, and dozens of whole blocks of real AC coefficients
decode cleanly against the same standard's Table B.15 — but at least one codeword does not match it,
and this investigation could not determine which of several equally-close candidates, if any, is the
real one.

## What the page states in prose, and what it prints as ffmpeg's own array

The MultimediaWiki "SpeedHQ" page is unusual among this file's sources for saying, in its own words,
that the vendor cooperated in writing it — a materially different footing from Canopus's silent papers
or MSS1/MSS2's unattributed page above. Most of it reads as genuine independent description: the field
and slice framing, the macroblock and block layout, the quantisation matrix and the allowed-quality
table, the DC prediction rule, and a prose account of alpha coding for the variants that carry it. One
part of the page is not independent description, and says so itself: a block of code headed "In FFmpeg
format, the codes are (except that they would need to be bit-reversed due to `INIT_VLC_LE` demands)",
printing `static const uint16_t speedhq_vlc[123][2]`, `speedhq_level[121]` and `speedhq_run[121]` —
C array declarations, ffmpeg's own variable names, and a comment about ffmpeg's own bit-reader macro.
That block is `libavcodec/speedhq.c` copied out, not documentation of it, and this project does not
transcribe or translate ffmpeg source regardless of who else contributed to the page it is quoted on.
It was read to know it exists and not used for a single bit pattern.

## What was verified independently of that array

A corpus was built with ffmpeg's own `speedhq` encoder — small `testsrc2` frames, both chroma
subsamplings the encoder writes (`yuv420p`, `yuv422p`), quality settings from `-qscale:v` 2 to 20 — and
read directly, byte by byte and bit by bit, against nothing but the page's prose and this project's own
already-verified MPEG-2 tables.

  - **The frame header** matches the page's own description on every file tried: byte 0 is a quality
    value with the quantiser equal to `100 - quality`, and the following three bytes are a little-endian
    offset to a second field — every file from ffmpeg's encoder states an offset of exactly 4, the page's
    own documented exception meaning a single progressive field rather than two interlaced ones, and
    every file's remaining bytes divide cleanly into that single field's slices with nothing left over
    but sub-byte padding.
  - **Bits are packed into 32-bit little-endian words and read from the least significant bit**, exactly
    as stated: reading a slice's bytes four at a time as little-endian words and concatenating each
    word's bits from bit 0 upward reproduces a linear bitstream in which every code below decodes cleanly
    in sequence; reading big-endian, or most-significant-bit first, does not get past the first slice's
    first block on any file tried.
  - **Slice chaining by a three-byte length prefix, including its own three bytes**, holds exactly:
    walking a field's bytes by each slice's own stated length lands precisely on the next slice's length
    field, with zero bytes left over past the last slice on every file tried, and the number of slices
    found always matches the field height divided by sixteen, rounded up.
  - **DC coefficients decode with ISO/IEC 13818-2's own Table B.12 (luminance) and Table B.13
    (chrominance)** — the exact tables this package's `MpegVlcTables.cs` already carries, transcribed
    from the standard for its own MPEG-2 decoder and reused here unchanged — together with the page's
    stated twist: **prediction restarts at 1024 at the start of each macroblock row, and the coded
    differential is subtracted from the prediction rather than added.** On a flat test frame, every
    block's coded differential and its effect on the running prediction were checked by hand against the
    frame's own uniform pixel value and came out exact; on textured frames, the predicted-then-corrected
    DC values move in the direction the source image's own gradients do, block by block, which a wrong
    table or a wrong sign would not produce.

## What was verified about the AC table, and where it stops

Real ISO/IEC 13818-2 Table B.15 — the second AC coefficient table, `MpegVlcTables.cs`'s own
`IntraCoefficient`, used for exactly the intra-only purpose SpeedHQ needs it for — was tried unmodified
as the AC coding, with the escape code read as the page states: six bits of run, twelve bits of level
offset by 2048. Across a dozen full macroblocks spread over several files, whole sequences of blocks —
DC through every AC coefficient to a terminating End of Block — decode with no unmatched code, run and
level values that stay small and plausible for real image content, and a slice's total bits consumed
landing within a handful of padding bits of the slice's own stated length. That is not the signature of
a wrong table: a table wrong in enough places to matter would drift a slice's bit count by much more
than padding across dozens of blocks, the same reasoning this project applies to Ut Video's and
MagicYUV's Huffman tables when their bit budgets close exactly.

It is not a hundred per cent match either. On a 64x32 `testsrc2` frame encoded at `-qscale:v 20`
(quantiser 40), the first macroblock's first two luma blocks decode completely — DC 1806 then 1694,
every AC coefficient found, both ending in a clean End of Block — and its third luma block's DC (1751)
and first AC coefficient (run 0, level 2) also decode cleanly, immediately followed, at bit 169 of the
slice, by a twelve-bit sequence — `000000011101` — that is not any code in Table B.15 at all. It is not
merely close to one wrong reading either: it sits at a Hamming distance of exactly one bit from four
different Table B.15 codes at that same length — `(run=3, level=3)`, `(run=7, level=2)`, `(run=17,
level=1)` and `(run=19, level=1)` — and distance two from three more, which rules out guessing a single
bit flip as the fix. Nothing short of independently knowing which coefficient the encoder actually
intended at that position — computed from the source picture through SpeedHQ's own stated
dequantisation and forward transform, the way this project's VP6 investigation used a forward DCT of
ffmpeg's decoded output as ground truth — would settle which candidate, if any, is real, and building
that ground-truth harness was not completed in the time this investigation had.

## Why this is not the same wall as Canopus, MSS1, MSS2 or DV above

Every other entry in this file that stops at a missing table stops because no independent description
of the table exists at all, or because the encoder needed to drive a corpus toward a specific codeword
does not exist. Neither is true here: ffmpeg's `speedhq` encoder writes real files to order, the vendor
is credited with helping write the documentation, and the great majority of both the DC and the AC
tables were confirmed, not assumed, against real bitstreams using tables this project already owns from
the ISO standard. What stops it is narrower and more specific — a small number of codewords, of unknown
count, that the page's own prose says are "moved around" relative to real MPEG-2 without saying where to
or how many — and closing that gap needs either a description of exactly which codewords differ from a
source that is not ffmpeg's own array, or the forward-transform ground-truth work this investigation
did not reach.

## What would change the answer

A statement of which Table B.15 codewords SpeedHQ reassigns and to what, from a source that is not
`libavcodec/speedhq.c` or a page quoting it — even a handful of examples would likely be enough to
recognise the pattern, if there is one, across the rest of the table. Failing that, a forward-DCT
ground-truth harness built from the page's own stated quantisation (a fixed DC divisor of 16, the
printed 8x8 matrix scaled by `100 - quality` for AC and divided by 16 without rounding, and the
DC-only shortcut `(dc + 4) >> 3`) run against ffmpeg's own decoded pixels would let each divergent
codeword be resolved by elimination the way this project's VP6 investigation attempted, rather than by
the single-bit-flip guessing that this investigation's evidence explicitly cannot support.

# Canopus HQ, HQA and HQX, where the vendor's own papers say nothing about the bitstream

Canopus HQ (`CUVC`) and its alpha-carrying sibling HQA share one ffmpeg decoder, `hq_hqa`; their
successor HQX (`CHQX`) is a second. Both were investigated on the strength of what `codec-coverage.md`
said about this whole family — "documented on MultimediaWiki" — and both stop at the same place MSS1
and MSS2 do above: the one detailed technical description of either bitstream is, by its own author's
account, notes from reverse engineering, and nothing independent of that exists to build from instead.

## What Canopus and Grass Valley published, and what it covers

Three white papers are linked from the MultimediaWiki pages for `Canopus_HQ` and `HQX`. Two resolve
through the Internet Archive after their original hosts — `canopus.com` no longer resolves at all, and
`grassvalley.com`'s copies 404 — went away: *The Canopus HQ Codec* (2004,
`web.archive.org/web/20071026184329/...cc_hqcodec_whitepaper70b72.pdf`) and *The Benefits of HQX*
(Grass Valley, May 2014, `web.archive.org/web/20140513073637/.../GV-6097M-3_HQX_WP.pdf`, read here in
full). A third, `GV-4097M_HQX_Whitepaper.pdf`, has only a truncated Wayback capture — 1,048,576 bytes of
a stated 1,254,057, cut off mid-file by whatever crawled it, `qpdf --check` reporting the cross-reference
table missing — and could not be read at all.

Both papers that could be read are marketing and comparison documents, not specifications, and neither
states a single bitstream fact. The HQ paper is a sampling-resolution and bitrate comparison against
DVCPRO HD and HDCAM, illustrated with oscilloscope photographs and side-by-side crops at different
bitrates — image quality, not image coding. The HQX paper compares PSNR and multi-generation
degradation against DNxHD, ProRes and AVC-Intra, explains 10-bit quantisation error in general terms,
and gives the codec's architecture as a three-box diagram — "Block Transform → Quantizer → Entropy
Encoder" — with two user-facing knobs, a bit-rate fraction and a quantiser aggressiveness slider, and
one sentence on the entropy stage: "a lossless, arithmetic coder, similar to algorithms like WinZip."
No header layout, no macroblock size, no quantiser matrix, no VLC table, no scan order and no mention of
the macroblock shuffle the MultimediaWiki stub for HQ says the codec uses appears anywhere in either
document. Neither `Canopus_HQ` nor `HQX` on MultimediaWiki carries more than one paragraph of its own —
"an ordinary intermediate codec... 8x8 DCT blocks... intra-only" — before linking out to these same
papers and, for both, to their libav decoder source directly: `libavcodec/hq_hqa.c` and
`libavcodec/hqx.c`.

No SMPTE Registered Disclosure Document was found for either codec — unlike Apple ProRes, whose
bitstream is RDD 36, publicised widely enough to be cross-referenced from Wikipedia, ffmpeg's own
documentation and general search results alike. Nothing comparable turns up anywhere searched for
Grass Valley or HQX by name; the RDD index itself renders no document list without JavaScript this
project does not run, so the index page's own listing could not be checked directly, only what search
engines and other sites say about it.

## The one detailed write-up found, and why it cannot be used

*Final Words on Canopus HQ, HQA and HQX*, Konstantin Shishkov's blog at `codecs.multimedia.cx`, May 2013,
is the only place a bitstream-level description of any of the three exists. It states, for HQ: 16x16
macroblocks, 4:2:2, predefined profiles by frame size (160x120 to 1920x1080) each naming a slice count
and "a macroblock shuffling order... like DV," sixteen selectable quantiser sets with four quantising
matrices apiece, split by luma and chroma — 128 matrices, about 80 of them unique — and interlacing
signalled per block. For HQA: the same tables and coding as HQ, with a flexible frame size, an alpha
component per macroblock and a coded block pattern selecting which of four luma blocks (and their
paired alpha and chroma blocks) are actually coded, the rest filled with zero — full transparency. For
HQX: frames split into 480-macroblock slices with every 16 macroblocks shuffled, DC coefficients coded
as differences from the previous one in the same macroblock component and Huffman-coded by a table
chosen from the component's bit depth rather than sent as a flat 9-bit number, and quantisation split
into a selectable quantiser plus two matrices — "two instead of seventy eight" — with the AC token
tables ("CBP + 3 DC + 6 AC tables") selected by which quantiser a block uses.

Every one of those is exactly the kind of fact a decoder implementation needs and a specification would
state — and the post says outright where they came from. Its own conclusion: "Reverse engineering all
those formats was obvious because they are not complex, obfuscated or C++." Shishkov is `hq_hqa.c` and
`hqx.c`'s own author in libav; this post is his account of reverse-engineering the formats he then wrote
those decoders from, not a citation of anything published by Canopus or Grass Valley. It is a paraphrase
of an implementation one step removed, the same shape the MultimediaWiki page MSS1's and MSS2's sections
above found and declined to use, for the same reason: this project does not transcribe or translate
ffmpeg or libav source, and a description that is itself derived from reading that source is the same
material at one remove, whether or not a single line of code appears in it.

Set the post aside and nothing is left to build from. The vendor's own papers describe image quality
and business benefits, not coding; MultimediaWiki's own text is one paragraph pointing at those same
papers and at the source directly; and there is no SMPTE, ISO or other standards-body document naming
either codec at all.

## Why blind reverse engineering does not reach it either

Every codec elsewhere in this file that was mapped from the bitstream alone had either a corpus that
could be driven toward a specific codeword, or an independently-sourced anchor to cross-check a guess
against. Neither exists here. `ffmpeg -h encoder=hq_hqa` and `ffmpeg -h encoder=hqx` both report the
codec known but no encoder available, so there is no way to manufacture a stream that isolates one
quantiser set, one matrix or one shuffle order from the rest — what samples exist are whatever real
files happen to be found. And what real files exist is close to nothing: `samples.ffmpeg.org` carries
exactly one, `V-codecs/CUVC/canopushq.avi`, alongside three of the codec's own Windows VfW DLLs (not
documentation, and not something this project runs or disassembles) and a several-line MPlayer
`codecs.conf`-style registration snippet — FourCC, DLL name, output pixel formats — with no bitstream
content of any kind; there is no `HQA` or `HQX` directory there at all. Shishkov's own
post states he had seen only one Canopus HQ sample and no HQA or HQX samples whatever when he wrote it,
and thirteen years on that has not changed. Sixteen quantiser sets of four matrices each, a macroblock
shuffle tied to a table of named frame-size profiles, and — for HQX — a Huffman table chosen by bit
depth are all large, multi-valued tables of exactly the kind this project's own Indeo and TrueMotion 1
investigations found cannot be recovered by reading files that are too small to carry them; one file,
with nothing to check a candidate reading against, is the same wall by a different route — there is
nothing here large or varied enough to triangulate eighty unique matrices or a shuffle order from.

## What would change the answer

A description of any of the three bitstreams from a source that states it is not derived from reading
an implementation — an actual Canopus or Grass Valley engineering document, a patent that prints the
quantiser matrices or the macroblock shuffle order, or a second reverse-engineering write-up that says
plainly how it was produced and from what, the way this project would need for MSS1 and MSS2 above.
Failing that, a substantially larger sample corpus — particularly for HQA and HQX, where none is known
to exist publicly at all — would still need an independent anchor to check a reconstructed table
against, since ffmpeg's decoder cannot be that anchor without the transcription this project does not
do.

# Mandsoft Screen Capture Codec (MSCC), where neither a description nor a single file exists

MSCC was investigated as the first of the screen-capture codecs `codec-coverage.md` still listed as
left. It stops before the others do, on the plainest evidence in this file: there is nothing to read.

No MultimediaWiki page exists for it under any name tried — `MSCC`, `Mandsoft Screen Capture Codec` and
a full-text search of the wiki for `MSCC` all return nothing. Mandsoft's own site, `mandsoft.com`, whose
product page a search engine's cache still describes as "Capture screen to AVI movie files," now
resolves to a GoDaddy domain-sale listing with no content of its own, current or archived through this
project's tooling, carrying nothing about the codec beyond the fact that it once existed. No SDK, no
format note, no developer page.

Nor does a single sample file. `samples.ffmpeg.org/V-codecs/` — the corpus this package's other screen
and lossless codecs were measured against — carries no `MSCC` directory and no loose `mscc`-named file
anywhere in its listing, checked directly rather than assumed. FFmpeg's own `fate-suite`, reachable over
rsync and richer than the public sample tree for exactly this kind of obscure format — it is what
supplied RSCC's corpus below — carries no `mscc` directory either, out of 306 top-level entries checked
by name. `ffmpeg -h encoder=mscc` reports no encoder, so none can be built. Every general web search
tried, including ones aimed specifically at a stray `.avi` built by Mandsoft's own "Screen Movie Studio"
product, turns up nothing but FFmpeg's own decoder source and pages that cite it.

This is a stronger negative than MSS1 and MSS2's above, not a weaker one: MSS1 and MSS2 at least have a
sample directory at `samples.ffmpeg.org/V-codecs/MSS1/` and `MSS2/` to be opaque *about* once no
independent description can be found. MSCC has neither the description nor the file, so there is no
route to it — not the paraphrased-implementation route this file's other entries take, and not the
blind bitstream-measurement route RSCC below was tried against, which needs files to measure.

## What would change the answer

A real sample — from a recovered Screen Movie Studio installation, a still-live mirror of Mandsoft's own
downloads, or a corpus this project has not found — would open the same blind-measurement route RSCC
below was tried against. A vendor document describing the bitstream, even informally, would open the
same route MSS1 and MSS2 above are waiting on. Neither exists today.

# innoHeim/Rsupport Screen Capture Codec (RSCC), where the destination is legible and nothing else is

RSCC (`ISCC`, `RSCC`) — Rsupport's codec for its `liteCam` recording products — was investigated second,
and stops further along than MSCC does: FFmpeg's `fate-suite` carries a real corpus for it, five files
built as a deliberate feature test rather than a recording, and a real structure was recovered and
measured against real bytes before the investigation ran out of road. It stops closer to Escape 124 and
SpeedHQ's place than to MSS1's: a packet framing fully solved and a partial delta-record structure, with
one field of that structure and its numeric coding still open.

## What exists to build from, and what does not

No MultimediaWiki page exists for RSCC under any name tried, and a full-text search of the wiki turns up
nothing. What surfaces instead is FFmpeg's and Libav's own doxygen-generated source pages and mailing-list
commit messages for `rscc.c`, and nothing else — no vendor document, no independent write-up, nothing
that reads as anything but the implementation itself restated by tooling. Those pages were not read
beyond confirming that this is all that exists; nothing from them informed anything below, which was
built and checked against real packet bytes and ffmpeg's decoded pixels only.

`fate-suite`'s `rscc/` directory holds five files built to exercise the format's pixel-format range
rather than to record a screen: `8bpp.avi` (854×480, one frame, palettised), `16bpp_555.avi` (320×240,
15 usable frames, RGB555), `24bpp.avi` (854×480, 58 frames, BGR24), `32bpp.avi` (320×240, 9 usable
frames, BGR0) and `pip.avi` (1760×968, 5 frames, BGRA). `ffmpeg -threads 1 -fps_mode passthrough` decodes
all five; the last packet of `16bpp_555.avi` and several of `32bpp.avi`'s are shorter than their own RIFF
chunk headers state, which ffmpeg reports as `Insufficient input` rather than decoding — a truncated
sample rather than a format variant, the same shape RealMedia's and Lagarith's truncated files take
elsewhere in this project, and the frames before the truncation decode and were used regardless.

## What was recovered from the packets themselves, and verified

**A key frame is one zlib stream carrying the whole raw picture, behind a fixed thirteen-byte header.**
`16bpp_555.avi`'s first packet opens `01 00 00 00 | 40 01 00 00 | f0 00 | 17 07 | 00`, then a valid
zlib stream (RFC 1950's own header checksum, the same test this package's TSCC decoder already applies).
Read as little-endian, that is a `1` of unknown purpose, then `320` and `240` — the stream's own width
and height, confirmed against `ffprobe`'s stream info — then `1815`, which is exactly the packet's own
1828 bytes less the thirteen-byte header, and a trailing zero byte. Decompressing the zlib stream that
follows yields exactly 153,600 bytes, `320 × 240 × 2` — the whole raw picture at this stream's own two
bytes a pixel, with no remainder. The same shape — count, width, height, exact remaining-length field,
then a zlib stream decompressing to exactly `width × height × bytesPerPixel` — was confirmed on every
sample's own first packet, at each file's own resolution and pixel depth.

**A delta frame's payload is a whole number of eight-byte records**, and this is exact rather than
approximate: across every delta packet checked in `16bpp_555.avi`, `32bpp.avi` and `pip.avi`'s
count-4 packets, the zlib stream's decompressed length divided by eight equals the packet's own leading
count field precisely, with no remainder on any packet tried — 60 records for 480 decompressed bytes,
64 for 512, 37 for 296, and so on, every one exact.

**Two of a record's four sixteen-bit fields are legible, and they are the destination.** Read as four
little-endian words, the third field runs from 0 to within eight of the frame's own width, rises in
scan order across a delta packet's own records with only the resets a multi-row update would produce,
and never differs from a multiple of eight across any record in any packet checked — consistent with a
destination X coordinate on an eight-pixel grid. The fourth field takes only the small values 8, 16 and
32 in the packets checked here, which is well short of the frame's own height and fits a destination row
index on the same eight-pixel grid better than it fits anything else tried.

**The remaining two fields do not resolve as a source position on the same grid.** Read the same way as
the destination pair, the first two fields of several records in `16bpp_555.avi`'s later delta packets
carry values above 240 and even above 320 — outside both of the 320×240 frame's own axes — which rules
out the plainest reading, a source pixel coordinate on the same picture the destination pair addresses.
Whether they are a coordinate on a different implicit surface, an offset rather than a position, or not
a position at all was not settled.

## Where it stops, precisely

At the mystery header field between a delta packet's leading count and its zlib stream. That field is
not a fixed width: across the packets checked it is a plain two-byte little-endian value when its low
byte is at or above 0xAA and a single byte otherwise — every packet with a two-byte field's low byte
seen so far is 0xAA or higher, every packet with a one-byte field is below it, with a consistent gap
between the two ranges (the highest one-byte value seen is 0x8A, the lowest two-byte low byte is 0xAA)
— but no reading of that split as a numeric quantity was found consistent with anything else measured:
not the record count, not the compressed or decompressed payload length, and no checksum of the
decompressed payload tried (Adler-32, CRC-32 or a plain byte sum, each truncated to sixteen bits) matches
it either. Confirming the field's true meaning would very likely also settle the header's real, principled
length rather than the length-by-trial this investigation used to reach the records at all.

The frame this project's own `pip.avi` sample uses for its "picture in picture" name compounds the
problem rather than side-stepping it: its delta packets carry a much larger prefix before their zlib
stream — 37 bytes ahead of a stated record count of 4, not the four bytes seen on the small files — which
does not divide into any clean number of same-shaped small records, and was not solved either. Whether
this is a second packet shape RSCC switches to under some condition, or the small-file shape with a field
this investigation has not identified running to a different length, was not determined.

A block-copy simulation using the two settled destination fields, a provisional eight-pixel block size
and the two unresolved fields taken at face value as a source position was tried against real decoded
frame pairs from `16bpp_555.avi` and does not reproduce the real next frame — the real byte-level
difference between consecutive decoded frames runs tens of thousands of bytes deep on packets whose
record count is far too small to state that much change under any block size the records themselves
state, which is itself evidence that the still-unresolved fields are not a simple source coordinate on
the previous frame's own canvas, whatever they are.

Nothing was shipped. A decoder that reproduced the destination grid correctly and guessed at the source
and the coding it is packed with would be exactly the failure this project holds itself to a stronger
rule than: a wrong still passage is indistinguishable from a working one, and a screen-capture codec
spends most of its frames on exactly that.

## What would change the answer

A description of the mystery header field's encoding and the two unresolved record fields' meaning, from
a source that is not `rscc.c` restated — or a larger, more varied sample corpus than `fate-suite`'s five
files, since a scheme this resistant to five files' worth of evidence may simply need more of it, the way
TrueMotion 2's entropy layer above needed thousands of streams rather than dozens to pin down with
confidence.

# WinCAM Motion Video (WCMV), where WinCAM's own screen codec has no independent trace at all

WCMV was investigated on the strength of sharing a vendor family with ScreenPressor below — both are
associated with WinCAM, the screen-recording product ScreenPressor's own GitHub mirrors describe as one
of its host applications — and stops on the same evidence MSCC does. No MultimediaWiki page exists under
`WCMV` or `WinCAM Motion Video`, checked directly and by search; every general search tried surfaces only
FFmpeg's own `wcmv.c` and mailing-list commit records referring to it, nothing that reads as independent.
No sample exists either: `samples.ffmpeg.org/V-codecs/` carries no `WCMV` entry, and FFmpeg's `fate-suite`
carries no `wcmv` directory among its 306 top-level entries. `ffmpeg -h encoder=wcmv` reports none. As
with MSCC, there is neither a description nor a file to build from.

## What would change the answer

The same two things MSCC needs: a real sample, or a vendor or independent description of the bitstream.
Neither was found.

# MatchWare Screen Capture Codec (MWSC), where one file exists and nothing describes it

MWSC was investigated next. It clears MSCC's and WCMV's first bar — `samples.ffmpeg.org/V-codecs/`
carries one file, `MWSC.avi`, decoding under ffmpeg to 8-bit palettised frames — but nothing describes
its bitstream. No MultimediaWiki page exists under `MWSC` or `MatchWare Screen Capture Codec`, by direct
lookup or search, and FFmpeg's `fate-suite` carries no `mwsc` directory to widen the one-file corpus with.
`ffmpeg -h encoder=mwsc` reports none, so nothing can drive a second file toward a specific codeword.

This project's own standard for a corpus already rules out building blind from one file before the
provenance question is even reached: every codec here that was mapped from bitstream measurement alone —
TrueMotion 2's entropy layer, RSCC's destination grid above, Ut Video's slice division, MagicYUV's
permutation — needed dozens to thousands of streams to pin a table or a rule down with confidence, and
this project's own MSZH entry above records what one real file (there, one still picture re-encoded six
ways) is worth: two genuine match tokens, neither resolved. One MWSC file is the same shape of evidence,
not more of it just because it happens to be a real recording rather than six re-encodings of a
photograph.

## What would change the answer

A second and third sample distinct enough to cross-check a reading against, or a description of the
bitstream from a source that is not an implementation. Neither exists today.

# RemotelyAnywhere Screen Capture (RASC), where neither a sample nor a description turned up

RASC was investigated fourth and stops on MSCC's and WCMV's evidence rather than MWSC's: no
MultimediaWiki page under `RASC` or `RemotelyAnywhere Screen Capture`, by direct lookup or search: every
search tried surfaces only FFmpeg's own decoder source and nothing that reads as independent of it. No
sample turned up either — `samples.ffmpeg.org/V-codecs/` carries no `RASC` entry, and FFmpeg's
`fate-suite` carries no `rasc` directory among its 306 entries. `ffmpeg -h encoder=rasc` reports none.

## What would change the answer

The same as MSCC and WCMV: a real sample, or a description of the bitstream from a source that is not an
implementation.

# Go2Meeting (G2M), where the one detailed page is the decoder's own author's account of it

Go2Meeting's screen codec (`G2M2`, `G2M3`, `G2M4`) was investigated fifth, flagged going in as large and
partly JPEG-based, and it stops on documentation grounds before its size becomes the deciding factor.

## What exists

MultimediaWiki's `GoToMeeting_Codec` page is genuinely detailed: a four-byte frame signature, a
chunk-based body with a one-byte type and a four-byte length, a 192×128 tiling scheme, six named chunk
types (display configuration, image update, cursor position, cursor shape, a resync marker and a
time-related chunk), and three compression paths — an entropy-coded-only path, an entropy-plus-JPEG
hybrid, and a deflate-plus-JPEG path — with the entropy coder described as context-modelled exponential
Golomb coding over neighbour-predicted pixel values. A real sample corpus exists to check any of it
against: FFmpeg's `fate-suite` carries `g2m/g2m2.asf`, `g2m3.asf` and `g2m4.asf`, and
`samples.ffmpeg.org/V-codecs/G2M4/` carries four further `.wmv` files.

## Why it was not pursued past that

The page states no provenance for any of it — no citation to a Citrix or GoToMeeting engineering
document, no note that it was reverse-engineered from the codec, nothing. That is the same silence MSS1's
and MSS2's pages carry, and this project's standard for that silence is not to take a detailed page at
face value merely because nothing on it announces where it came from: MSS1's and MSS2's own pages passed
that same silent test until their function names were checked against `libavcodec/mss2.c`'s and found to
match one for one, shared typo included. No comparable public FourCC-string check was performed here —
this section did not read `libavcodec/g2meet.c` or any description of it to compare against, the same
restriction this project holds everywhere else — so the page's independence was not established, only
left unconfirmed. What tips this from "unconfirmed" to "not pursued" is that Go2Meeting's screen codec
is, on this page's own account, three coding paths deep, one of them a JPEG hybrid needing its own
tile-boundary and quantisation handling worked out on top of the entropy coder — the largest and most
structurally involved format on this list bar TSCC2's DCT path — and building that on a page whose
independence could not be checked either way is not a wager this project takes, on the evidence MSS1,
MSS2, Canopus and TSCC2 below all give for what such a page usually turns out to be.

## What would change the answer

A statement of where the `GoToMeeting_Codec` page's description came from — confirming it as an
independent account rather than a paraphrase of an implementation — would open a large, real sample
corpus to work from. Absent that, the same function-name or constant-matching check this project used
for MSS1 and MSS2 against a suspected source, performed by someone willing to read that source directly
to make the check (which this investigation does not do), would settle the question either way.

# ScreenPressor (SCPR), where the only "documentation" is somebody else's open-source rebuild

ScreenPressor was investigated sixth. `samples.ffmpeg.org/V-codecs/` carries one file, `SCPR.avi`, and
`ffmpeg -h encoder=scpr` reports none, so the same one-file ceiling MWSC stops at applies here before the
documentation question is even reached. No MultimediaWiki page exists under `ScreenPressor` or `SCPR`,
by direct lookup or search.

What search turns up instead is two GitHub repositories, `yarrom/ScreenPressor` and
`thedeemon/screenpressor`, both styled as open-source rebuilds of Infognition's proprietary codec rather
than anything Infognition itself published. Both are implementations — exactly what this project's rule
against transcription covers regardless of who wrote them or under what licence — and neither was opened
or read beyond confirming what they are from their own repository descriptions. Using either to write a
decoder here would be the identical problem this project already declined for MSZH's and Lagarith's own
implementation-only descriptions, applied to a second author's code instead of the vendor's or ffmpeg's.

## What would change the answer

A statement of the bitstream from Infognition itself, or from an independent party who did not derive it
by reading ScreenPressor's own code or a rebuild of it — plus, regardless, several more real samples than
the one file found, on the same evidence MWSC's entry above gives for why one file is not a corpus this
project builds a table from.

# Screenpresso, where neither a sample nor a description turned up

Screenpresso was investigated seventh and stops on MSCC's, WCMV's and RASC's evidence: no MultimediaWiki
page under `Screenpresso`, by direct lookup or search — every search tried surfaces only the Screenpresso
application's own marketing pages and FFmpeg's decoder source, nothing independent describing its
bitstream. No sample exists either: `samples.ffmpeg.org/V-codecs/` carries no `screenpresso` entry, and
FFmpeg's `fate-suite` carries no matching directory among its 306 entries. `ffmpeg -h encoder=screenpresso`
reports none.

## What would change the answer

The same as MSCC, WCMV and RASC: a real sample, or a description of the bitstream from a source that is
not an implementation.

# TechSmith Screen Codec 2 (TSCC2), the DCT-based codec that shares only a vendor with TSCC

TSCC2 was investigated last, as the brief for this batch of work expected: despite the name, it is not a
variant of this package's own TSCC decoder — DEFLATE over a run-length coding — but a block-transform
codec, and it stops on the same evidence Canopus HQ, HQA and HQX do above.

## What exists

FFmpeg's `fate-suite` carries one sample, `tscc/tsc2_16bpp.avi`, alongside the unrelated TSCC files that
directory's name suggests. The one technical description found anywhere is Konstantin Shishkov's own
blog, `codecs.multimedia.cx`, in the same 2012 post this package's Canopus investigation above already
found describing HQ, HQA and HQX: internally named "Dora," splitting a frame into 16×8 slices and those
into 4×4 blocks coded in 4:4:4 at a 16–240 range with a DCT-like transform and one of two quantisers, with
VLC tables for DC, a "number of coefficients" field and AC values.

## Why it was not pursued past that

Shishkov is `tscc2.c`'s own author in FFmpeg, exactly as he is `hq_hqa.c`'s and `hqx.c`'s in libav, and
this post is the same shape of document this project already declined for those three codecs above: his
own account of reverse-engineering a format he then wrote a decoder from, not a citation of anything
TechSmith published. TechSmith's own pages for Camtasia and the TSC2 codec describe the download and the
product, not the bitstream, the same gap Canopus and Grass Valley's marketing papers leave for HQ and
HQX. Nothing independent of that one post was found anywhere searched.

## What would change the answer

The same as Canopus HQ, HQA and HQX above: a description of the bitstream from a source that states it is
not derived from reading an implementation, or a second reverse-engineering write-up that says plainly how
it was produced and from what.
