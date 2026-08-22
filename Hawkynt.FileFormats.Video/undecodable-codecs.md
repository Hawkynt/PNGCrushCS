# Codecs investigated and not implemented

Every codec below was investigated and none was implemented. That is a result rather than a gap, and
it is written down here so the work is not repeated by somebody who assumes it was never attempted.
The running count lives in `codec-coverage.md`, which is where it is kept correct; one section here
sometimes covers several of the decoders counted there, so the two are not the same number and were
never meant to be.

They stop in four different places, and the four are worth keeping apart. Five need constant tables
that are not in the file, and WMV1 and WMV2 join them on the same evidence, tied to MS-MPEG4v3's own
already-missing tables by shared escape constants rather than by file size. VP6 and VP5 stop
somewhere else entirely: VP6's tables **are** published, every one of them was transcribed and
checked, and the decode still does not come out. Lagarith stops at a third place again — its wrapper comes out
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
— that this project's evidence could narrow down but not close. VP4 stands apart from all of these: its
two independent sources name their own method rather than paraphrasing somebody else's decoder, and
this project went further than reading them, obtaining and running On2's own `vp4vfw.dll` directly and
verifying an independent bit-level parser against it frame by frame — and it still stops at exactly the
one family of tables neither source prints, the motion-vector Huffman codes, which the codec's own
binary stores as branches rather than as data reachable by inspection. VP7 is the cleanest case of the
first place this project has found: not a secondary source or a reverse-engineer's notes but On2's own
"VP7 Data Format and Decoder," predating ffmpeg's own VP7 decoder by nine years, naming its own
reference decoder's two source files — `quant_common.c` and `findnearmv.c` — at the exact two points,
the dequantisation tables and the interframe motion-vector census, where it declines to print what
those files hold. Where each stops is recorded below.

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

None of the twenty-seven had anything committed.

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

# On2 VP4, where the wiring is published and one wire is not

VP4 — FourCC `VP40` — was investigated first among the two On2 codecs this task targeted, on the
strength of a real advantage the note above already gives it: it shares almost all of its structure
with VP3, which this package decodes exact over 3,182 frames. That advantage turns out to be real for
everything except one family of tables, and this section records exactly where the line falls,
verified against the codec's own binary rather than against anyone's description of it.

## Provenance, checked before anything was trusted

Two sources describe VP4's bitstream: the MultimediaWiki `On2_VP4` page, and three posts on Kostya
Shishkov's `codecs.multimedia.cx`, the most detailed being "Some notes on VP4" (2015) and "General
overview of Duck codecs and their design" (2020). Both name their own method in the open, which is what
the MSS1/MSS2 and Canopus HQ sections above found missing and declined to use on that account: "REing
VP4 is rather easy — you just download original VP3.2 decoder source (still available at Xiph SVN
servers) and compare it to the structure in `vp4vfw.dll`." That is a first-party account of reading
On2's own shipped binary against On2's own already-public VP3.2 source, not a paraphrase of somebody
else's decoder — the same category of source this project already accepts from Lagarith's author's own
2006 wiki page — and the account is candid about its own gaps: several header fields are marked "I
didn't care enough to decipher their meaning though," which is not how a page copying a working,
complete implementation reads.

## What was recovered from those two sources

  - **The shared frame prefix**: 1 bit frame type, 1 bit unused, 6 bits of "DCT Q mask" (a quantiser
    index), for every frame.
  - **The CBP scheme for non-key frames** in full: `MBFullyFlags[]` and, for macroblocks it leaves
    zero, `MBCoded[]`, both run-coded flag arrays; then, for a macroblock `MBCoded` marks, a
    Huffman-coded coded-block pattern read from one of two 14-entry tables, switching to the second
    whenever the value just read was `0x3`, `0x7`, `0xD` or `0xE`. Both tables are printed to the
    codeword.
  - **The flag-array code** in full, a hand-rollable exp-Golomb-like scheme (`get_mplayer` in the
    source's own naming) that this project reimplemented directly from the printed pseudocode.
  - **Four structural facts, each a genuine departure from VP3** that the two sources between them
    name plainly: coefficient tokens are grouped by block rather than by frequency across the frame;
    DC prediction averages the two available neighbours, or falls back to the last predicted value,
    rather than VP3's weighted sum; the loop filter runs during motion compensation against the
    prediction rather than once over the whole reconstructed picture, so reference frames carry no
    filtering of their own; and motion vectors are Huffman-coded per component, the table chosen by
    `log2(ABS(last_component))`, with the sign taken from the previous vector rather than read fresh.
  - **What is not stated anywhere in either source**: the motion-vector Huffman tables themselves —
    the wiki gives the *scheme* (`get_vlc(hufftab_mvx[log2(ABS(last_mv_x))])`) and never the table —
    and the meaning of roughly fifty bits in the key-frame header, six fields the wiki marks `???` and
    states only the width of, immediately after a named `version byte 0` (8 bits), `version` (5 bits,
    "should be 2"), `key frame type` (1 bit) and 2 spare bits.

## What this project verified independently, against the codec itself

Nothing above was taken on faith. `samples.mplayerhq.hu/V-codecs/VP4/` — the address the wiki page
itself gives — carries not only a sample (`ot171_vp40.avi`, 160×112, 364 frames, md5-checked against
its own manifest) but the actual On2 `vp4vfw.dll` (4.0.20.24, same directory, same manifest), placed
there for exactly this kind of work. `ffprobe`/`ffmpeg` decode the sample cleanly (`yuv420p`, one key
frame, `-fps_mode passthrough` matching `ffprobe -count_frames` exactly), so it is a usable oracle, and
the DLL is a second, independent one: On2's own decoder, not a paraphrase of it.

  - **A harness was built and run.** No native 32-bit toolchain exists in this environment, so one was
    assembled from what does: `clang -target i686-pc-windows-gnu` compiling against Wine's own
    `i386-windows` import libraries (`vfw.h`, `libkernel32.a`) with a handful of freestanding stub
    headers for the pieces no libc was available to supply, linked with `lld-link`, run under
    `wine` (11.15). The harness calls `vp4vfw.dll`'s exported `DriverProc` directly through the
    documented Video-for-Windows protocol (`DRV_LOAD`, `DRV_OPEN` with an `ICOPEN` naming `VP40`,
    `ICM_DECOMPRESS_GET_FORMAT`, `ICM_DECOMPRESS_BEGIN`, `ICM_DECOMPRESS`) — all Microsoft's own public
    ABI, no different from using a documented file format's header layout.
  - **It decodes.** Frame 0 (the key frame) comes back `ICERR_OK`; converted the same bottom-up BGR24
    way any DIB is and compared against `ffmpeg`'s decode of the same frame, every sample lands within
    16 of ffmpeg's — the shape this project's own notes describe as ordinary YUV-to-RGB rounding, not a
    structural mismatch, and the frame's content and orientation both check out by eye. Frame 1, an
    inter frame, also returns `ICERR_OK` in sequence against the same open instance.
  - **The published pieces were reimplemented independently and checked against the real bitstream,
    bit by bit, with no reference to any decoder's source.** A from-scratch parser — the shared
    8-bit prefix, `get_mplayer`, both CBP tables with the context switch, and VP3's own macroblock
    coded-order geometry and mode-scheme Huffman table (`Vp3Geometry`, `Vp3ModeReader`, already in this
    tree) reused verbatim, since the wiki states the mode data is "coded in the same way as in VP3" —
    was run against frame 1's real bytes. It parses to the bit with no desync: 70 macroblocks for the
    160×112, 10×7 grid; 69 read fully coded and one partially, whose Huffman-coded CBP comes out
    non-zero; a mode scheme of 7 (the three-bit literal form) reading a sensible histogram — 25
    no-motion, 6 intra, 7 single-vector inter, 8 last-vector, 6 last-2-vector, 7 golden-no-vector, 3
    golden-with-vector, 8 four-vector — that lines up with VP3's own `ReferenceOfMode` table with no
    forcing. That lands the parser at bit 252 of the packet's 9,280, precisely at the motion-vector
    section, needing 42 individual vector components from here (7, plus 3, plus 8 fours) that the two published
    sources describe the shape of and print no table for.
  - **A search for the tables as data, not as description, came back empty.** The CBP tables are known
    exactly, so they make a positive control: every code/value and length/value encoding this project
    could construct for them was searched, byte for byte, against the DLL's `.rdata` on disk and
    against its live `.data` section dumped from the running Wine process after decoding the key frame
    and again after decoding the following inter frame (the two dumps are byte-identical, so nothing is
    built lazily into that section between the two, and it is not where a runtime-constructed table
    would appear either). None of these encodings appears anywhere in either. That is informative on its
    own: it says the codec's own C source almost certainly decodes these small alphabets with inline
    branches rather than a lookup table, which is exactly why the CBP tables the wiki does print had to
    come from someone reading disassembled code rather than a data section, and why the same route
    would be needed for the motion-vector tables this project does not have.
  - **A live decoded frame's heap was inspected**, and a window of small `int16` pairs matching plausible
    motion vector magnitudes (0, ±1, ±2) was found in the codec's per-instance state between decoding
    frame 0 and frame 1 — genuine evidence the DLL is really doing motion compensation on this stream —
    but its count (25 pairs) does not match the 42 raw vector reads the bitstream parse above requires,
    so it is some derived or partial array rather than a one-to-one trace of the codewords read, and
    was not something a table could be reconstructed from without knowing which of those two counts,
    and which order, it actually holds.

## Where it stops, and why further static reading does not close it

The wall is exactly the one the two published sources already drew, now confirmed rather than assumed:
the per-component, magnitude-bucket-indexed motion-vector Huffman tables are not printed anywhere this
project found — not on the wiki, not in any of Kostya's three posts, not in a patent or an academic
paper, not in any GitHub or archive.org listing a search for `hufftab_mvx` or equivalent turned up —
and they are not recoverable from the DLL by the means available here. They are used from the first
inter frame of the first real sample, so a decoder that cannot read them cannot decode motion at all,
which is most of what a real VP4 file spends its bits on. The roughly fifty unstated bits of the
key-frame header are a second, smaller unknown in the same direction: their width is published, their
meaning is not, and nothing here tried skipping them at the stated width against the oracle, because a
working key frame alone — without the inter frames the same stream needs the missing tables for — would
not be a working decoder by this project's own standard.

Recovering the missing tables from the binary directly, the way Kostya's own account says he read the
CBP tables and the frame header shape, is possible in principle — this project got as far as a
correct, working, independently-built harness that runs the real decoder and inspects its live state,
which is further than a description of the codec alone would allow — but doing so needs the actual
decode routine disassembled and stepped through instruction by instruction to find where the codeword
is read and matched against a symbol, not a data section searched for a table that turns out not to be
stored as one. That is a different, and considerably larger, piece of work than this investigation
completed, and nothing here should be read as ruling it out for whoever picks it up next: the DLL, the
sample, and a cross-compiler that reaches it without installing anything are all confirmed to exist and
to work.

## What would change the answer

A published account of the motion-vector Huffman tables from a source that names its own method the
way the two used here do — or a completed disassembly of `vp4vfw.dll`'s decode routine, for which this
investigation's harness and verified frame-by-frame bit parser are a working starting point rather than
something to redo. Failing either, the same two things would help on the header: a stated meaning for
the key frame's fifty unpublished bits, or a demonstration that a fixed-width skip past them is safe to
assume, checked the way this project checks anything else — against the oracle, over real files, not
by inspection alone.

# VP7, where the document names the two files it will not print

VP7 (`VP70`, `VP71`, `VP72`) is VP8's immediate ancestor, and On2 published its own specification for
it — "VP7 Data Format and Decoder," document version 1.5 of March 28, 2005, mirrored at
`multimedia.cx/mirror/VP7_Data_Format_and_Decoder_Overview.pdf` — nine years before ffmpeg's own VP7
decoder existed (the patch is dated February 2014), so the direction of dependence is the right way
round. It is a complete document in the way VP6's is: sixty-five pages, a full worked description of
the boolean coder, the frame header, every intra prediction mode, the coefficient tree, and VP7's own
4x4 DCT — not VP8's Walsh-Hadamard-and-butterfly pair, but a real DCT-II, given as a complete,
unambiguous C fragment. Samples came from `samples.ffmpeg.org/V-codecs/VP7/`, an AVI wrapper (fourcc
`VP70`) around each; ffmpeg decodes them, so it remains a usable oracle.

## What was verified

Everything the document prints was checked against ffmpeg's decode of two of those files —
`potter-40.vp7` and `potter-700.vp7`, the same footage (an MPAA ratings-board leader) at two
bitrates and two different quantiser indices, 320x176 and 624x352. The boolean coder is VP8's own,
formula for formula (`split = 1 + (((range-1)*prob)>>8)`), which this project already has and reused
rather than re-deriving. A striking number of the document's other tables and small arrays turn out to
be VP8's own too, printed as the same numbers rather than merely the same shape: the key frame subblock
mode probabilities (nine hundred entries, matching RFC 6386's from the first row on), the default token
probability table's printed portion, the coefficient bands, the default scan order, the six-tap
sub-pixel filter, the small-motion-vector tree, the category extra-bit probabilities, and several of
the default mode probability arrays. Two of the document's own small tables are left as `{ ??, ??, ?? }`
and `{ ?? }` in both the rendered PDF and its text layer — key frame chroma mode defaults and the
interframe subblock mode defaults — and VP8's RFC 6386 values were used in their place on the same
evidence.

The frame tag, the boolean-coded picture dimensions, the four macroblock "feature" records (VP7's
replacement for VP8's segmentation), and the five optional quantiser-index overrides were all checked
bit for bit by an independent, from-scratch decoder written in Python against the same two files'
headers, and agree with this project's own C# decoder exactly — width, height, every feature disabled,
base quantiser index 17 for the 320x176 file and 8 for the 624x352 one, every one of the five optional
indices defaulting to the base as their flags said. Intra prediction's formulas were compared against
VP8's (RFC 6386, 12) by hand rather than against real pixels — the quantiser gap below means no
macroblock with any AC coefficient in it can be checked against the oracle yet — and they match exactly
except for one confirmed, isolated difference: VP7 substitutes the single value 128 for a sample outside
the picture on every side, where VP8 uses 127 above and 129 to the left. VP7's own 4x4 DCT-II was
implemented from the document's complete C fragment and checked by hand against its own arithmetic: for
a DC-only 4x4 block the two-pass, fixed-point computation this project wrote reproduces, digit for digit,
the values a plain evaluation of the document's own formula gives, and that same DC-only case is the one
place the transform's output was also checked against a real file — see below.

## Where it stops, precisely

At the dequantisation factors — chapter 14 names the file they come from, `quant_common.c`, and prints
none of them.

The evidence is a single macroblock: the top-left 16x16 of frame 0 in both files is a flat area of the
ratings-board card, decoding to a perfectly uniform block in both this project's output and ffmpeg's,
which confirms on its own that no AC coefficient survives anywhere in it — the discrepancy is a pure DC
scale. In the 320x176 file (quantiser index 17) this project decodes that block to 98; ffmpeg decodes it
to 93. In the 624x352 file (quantiser index 8) this project decodes it to a different but equally flat
wrong value; ffmpeg again gives 93. Since the DC path for a macroblock with no Y2 residue elsewhere in
it runs the token's dequantised value through the document's own two-pass transform twice — once to
invert the "second order" Y2 block, once more for the luma subblock the result is scattered into — the
exact dequantisation factor needed to land on 93 can be computed by inverting that arithmetic, which is
itself fully specified and already checked. It is 44 at index 17 and 23 at index 8. VP8's RFC 6386
dequantisation tables, doubled the way VP8's own Y2 DC factor is derived from its DC lookup, give 38 and
22 — close enough to look plausible and wrong both times, and not by any single additive or
multiplicative adjustment that fits both indices at once. Chapter 16.3 is the same shape of gap for a
second, independent reason: the probability table that picks a macroblock's motion vector reference is
"calculated, using already-decoded motion vectors in (up to) 12 nearby blocks, by a fairly elaborate
process best described by the reference implementation itself (the function `FindNearMVs` in the file
`findnearmv.c`)" — named and left undescribed, the same way `quant_common.c` is, and its own
`ModeContexts` probability table has thirty-one rows where VP8's equivalent has six, which is further
evidence the two algorithms are not the same one at a different scale.

Neither file is available from a source this project will read. VP7 was never open-sourced the way VP8
was — On2's reference decoder was licensed, not published — and the only place either constant lives
today is ffmpeg's own `vp7.c` and `vp8data.h`, written from that unavailable reference nine years after
On2's own document was current. A web search for the quantiser tables surfaced only the *names* ffmpeg
gives them — `vp7_ydc_qlookup`, `vp7_yac_qlookup`, `vp7_y2dc_qlookup` and `vp7_y2ac_qlookup`, distinct
from and not simply derived from VP8's own `vp8_dc_qlookup` and `vp8_ac_qlookup` — which is itself
useful confirmation that VP7 carries its own tables and not VP8's borrowed forward, but the numbers
inside those tables were not looked at, on the same rule that held a PR for quoting ffmpeg comments
verbatim: reading the file to extract a constant is reading the file.

## What is already solved, if this is picked up again

The boolean coder, the frame tag and boolean-coded dimensions, the macroblock-feature header layout and
its per-macroblock encoding, and the quantiser index parsing are all checked bit for bit against real
files by two independent decoders and need no revisiting. VP7's own 4x4 DCT-II is checked by hand against
its own specified arithmetic and against the one real-file case its output could be isolated in. Intra
prediction's eleven modes are checked against VP8's RFC 6386 formulas by direct comparison, not yet
against real pixels — nothing but a DC-only macroblock can be checked until the quantiser is right, so
that comparison, and everything to do with interframe decoding (which needs the motion-vector census to
reach any macroblock at all), is exactly what a correct quantiser would unlock next rather than something
already closed. What would unblock this is a description of `quant_common.c`'s four (or six) constant
tables and of `FindNearMVs`'s twelve-block census from a source that states, or can be shown, not to be a
restatement of ffmpeg's or any other implementation — the same standard 8BPS's document cleared and
MSS1's, SpeedHQ's and TSCC2's did not.

## What would change the answer

An independent publication of VP7's quantisation tables and of the `FindNearMVs` algorithm, sourced the
way the rest of the document already is: from On2 itself, or from a description that predates and does
not derive from ffmpeg's reverse-engineered decoder.

# Sorenson Video 1 (SVQ1), where the codebook is the whole codec and nobody has printed it

SVQ1 — Sorenson Vector Quantizer 1, FourCC `SVQ1` — is a hierarchical multistage vector quantizer over
16x16 blocks down to 4x2, with mean removal and motion compensation between frames. The brief for this
investigation named the question to ask before writing anything: is that codebook published anywhere
independent of an implementation, or does it live only in one? It lives only in one, and the sole
detailed technical document on the format says as much about itself.

## The one technical document, and what it cites instead of printing

The only bitstream-level description of SVQ1 anywhere is "Description of the Sorenson Vector Quantizer
#1 (SVQ1) Video Codec" by Mike Melanson and Ewald Snel, published at `multimedia.cx/svq1-format.txt`;
MultimediaWiki's own SVQ1 page states outright that it "is based on" that document, so the two are one
source rather than two. It explains the algorithm's shape in real detail — mean removal, the multistage
codebook search, the 16x16-down-to-4x2 hierarchy, the interframe motion modes — and prints not one
codebook entry or VLC code. Its own reference list gives the reason: three FFmpeg CVS files —
`svq1_cb.h`, `svq1_vlc.h` and `svq1.c` — are named as where the tables actually are, and the document
says so directly rather than reproducing them: "All of these data tables can be found in the CVS source
repository for the ffmpeg project." That is a citation into an implementation for the one thing a
decoder cannot do without, not an independent publication of it — the same shape MSS1's page had with
FFmpeg's `mss2.c`, here made explicit by the document's own words instead of inferred from matching
identifiers.

The document's other cited source, US Patent 5,844,612 (*Motion vector quantizing selection system*,
Israelsen, assigned to Utah State University Foundation — the university whose licensed technology
became Sorenson's), is genuinely independent of FFmpeg, and was read on its own merits for exactly that
reason. It describes the same shape at the method level: codebook memory holds "the VQ comparison
codebooks" and the specification states its total size, and "all Huffman tables are stored in writable
tables" — but nowhere does it give a table's actual contents, only that tables of a certain size exist
and where they sit in the hardware. A patent claims a method; it is not a place a lookup table gets
printed, and this one does not print one.

## Why this settles it the same way it settles four codecs already in this file

The argument at the top of this document already covers the shape of the problem: a table this large —
a hierarchical multistage codebook covering four block sizes — comes from the file or from the decoder,
and if it is the decoder, this project does not transcribe implementations to recover it. SVQ1 does not
even reach the question this document's four smallest-frame codecs had to ask, whether a given sample
carries enough bytes to hold such a table, because the codebook is not carried per-stream at all: it is,
in the one document's own words, "hardwired" into the coding scheme, identical in every SVQ1 file that
exists. No frame is ever going to be the one that turns out to carry it.

## What would change the answer

A description of SVQ1's codebook and VLC tables from a source that is not an implementation — a genuine
Sorenson technical document, or a second patent that prints the tables themselves rather than describing
their existence and size. Neither exists today.

# Sorenson Video 3 (SVQ3), an H.264 draft with nobody publishing where it departs

SVQ3 — Sorenson Vector Quantizer 3, FourCC `SVQ3` — sits differently from SVQ1: this package already
carries a verified H.264 decoder (`Codecs/H264/`), and every account of SVQ3 agrees it is a variant of
that same family — the reason to look at it at all. What stops it is not a shortage of shared machinery;
it is that the parts where SVQ3 diverges from H.264 are exactly the parts nobody has published
independently of the one implementation that reverse-engineered them.

## What the container hands over, verified directly against a real file

This much needed no secondary source at all. `gl2.mov`, fetched from
`samples.ffmpeg.org/V-codecs/SVQ3/`, carries a standard QuickTime `ImageDescription` sample entry for
its video track — the fixed 86-byte structure Apple's own QuickTime File Format documentation defines,
independent of any codec — with `width` and `height` fields reading 470 and 352 directly out of the
file's bytes at their documented offsets, matching `ffprobe`'s report of the same file exactly.
Immediately after that fixed structure, at the position the sample entry's own stated size implies,
sits a nested atom named `SMI `, and inside it a four-byte marker `SEQH` followed by a four-byte
big-endian length and that many bytes of payload — 21 bytes for the `SMI` atom as a whole in this file,
matching `ffprobe`'s independently reported `extradata_size=21` exactly, and 5 bytes of `SEQH` payload
itself. That confirms the extradata wrapper's existence and shape from the file's own bytes: `SEQH`-
prefixed sequence data nested in an `SMI` atom inside the `ImageDescription`, the way any other
QuickTime codec extension sits — nothing about that part depended on reading anyone's decoder.

## Where the container stops mattering and the sourcing collapses

What is inside that `SEQH` payload — width/height codes, half-pel and third-pel motion flags, the
B-frame flag — and everything in the coded picture data after it — the entropy coding, the macroblock
type tables, the residual coding, the intra-prediction departures from H.264 — has exactly one technical
description anywhere: MultimediaWiki's Sorenson Video 3 page. That page names no source for almost
everything on it. Its introduction is one unattributed sentence — "Video codec apparently based on an
early H.264 draft" — and the one place it does cite something, it cites `svq3.c` itself, labelling its
own quantizer table "Quantizer table (from svq3.c)." The rest of the page's content — the Golomb-coded
macroblock type tables, the `SEQH` bit layout, the thirdpel interpolation formula, the intra-prediction
departures from H.264 — carries no citation to anything at all, and how anyone learned any of it is on
record independently of the wiki page: SVQ3 was closed and playable only through Apple's own QuickTime
component for years, until, as its own recorded history has it, an anonymous FFmpeg developer
reverse-engineered it and wrote the decoder that has carried it ever since. That decoder is not a
second, independent source standing next to the wiki page — on the page's own admission for the one
table it names outright, and on the format's own documented history for everything else, it is the
source the page paraphrases.

No IETF draft, ITU-T H.26L committee document, or Sorenson-assigned patent describing this format's own
departures from an early H.264 draft turned up in any search that reached the datatracker, the patent
literature, or Sorenson Media's own public output. The closest thing to a Sorenson-authored technical
document is the Library of Congress's format-sustainability page for Sorenson Video 3, and it states the
codec's marketing description — variable bitrate encoding, temporal scalability, "advanced vector
quantization with motion compensation" — and not one bitstream fact.

## Why the verified H.264 decoder does not rescue it

Reusing this package's H.264 work is exactly right where H.264 and SVQ3 coincide, and that was the
starting assumption of this investigation — but the two do not coincide at the layer that decides
whether a stream parses at all. SVQ3's documented departures are not cosmetic: a different entropy code
from either of H.264's own two (the one fact everyone credits it with, uncited, is that "this codec
extensively uses Golomb coding," which is neither H.264's CAVLC nor its CABAC), its own macroblock type
tables per frame type, and motion vectors carried at up to third-pel precision with per-macroblock
precision selection that H.264 has no field for at all. A decoder built from the public H.264 standard
alone desynchronises at the first macroblock-type codeword, because the standard does not define the
code SVQ3 uses there.

Recovering that code by measurement alone — the way this project recovered TrueMotion 2's Huffman trees
or Indeo 3's sixteen-byte table, from real bitstreams and an oracle, never from a description — was not
attempted at that depth here. The difference from those two is where the unknown sits: TrueMotion 2's
and Indeo 3's undocumented pieces are single tables reachable at a known bit offset once the surrounding
structure is parsed. SVQ3's is the entropy decoder itself, so nothing after the first unknown codeword
in a slice can be trusted to be at the right bit offset to test against — the same wall On2's VP6 stops
at with a full published specification in hand and years of the community's own effort behind it, and
there is no specification here to be even the eight decisions VP6 manages before it goes wrong.

## What would change the answer

A description of SVQ3's own departures from its H.264 draft ancestor — the entropy code, the per-frame
macroblock type tables, the `SEQH` field layout, the thirdpel interpolation and its motion-vector
precision signalling — from a source that is not FFmpeg's own decoder or a page that names no source at
all. Failing that, a blind reconstruction of the entropy layer against real samples, in the manner
TrueMotion 2's tables were recovered here, is the only route left; it was not carried past the container
in this pass, and the container-level facts above — the `ImageDescription` and `SMI`/`SEQH` wrapper,
both confirmed against a real file rather than assumed — are recorded so whoever attempts it does not
have to start from the container.

# Smacker, where the tree algorithm is right and the thing built out of it is not

Smacker is RAD Game Tools' own FMV format, and its container is read here — `Formats/Smacker`, merged
and measured against `ffprobe` packet for packet on six real files. The codec is not, and it stops in
an unusual place: not at a missing description, and not at a table nobody printed, but at a
composition step whose prose exists, reads the same in every revision of the only document that
covers it, and does not produce anything resembling what the files contain.

## What is confirmed correct

The base tree algorithm — the Tag/Flag/Leaf recursive descent every one of Smacker's Huffman trees is
built with — was settled independently and then confirmed twice over:

  - **Bit order is least-significant-bit of each byte first**, with the first bit read becoming the
    least significant bit of the output. Derived by arithmetic on the worked example in the February
    2006 revision of the format description, which predates any decoder built from it.
  - **Tree construction is a plain recursive pre-order walk**, the zero subtree built out in full
    before the one subtree.

The 2006 revision's own worked diagram contradicts the second of those. Its bottom rows read `(5) ( )`
then `(3) (4)`, which no reading of its own prose produces. A later revision of the same page silently
corrects the diagram to `(3) ( )` then `(4) (5)` — a plain pre-order walk, matching what the prose
says and what was implemented here from it. That later revision has been edited by the author of a
decoder for this format and so is not a source this project builds from; it is recorded only as
independent confirmation that the 2006 diagram was a transcription error and the reading taken here
was right.

## Where it stops

Everything above is the *base* algorithm. Smacker's four sixteen-bit tables — `MMap`, `MClr`, `Full`
and `Type` — are not built with it directly. They are built by a composition described in the format's
"Optimized Compression" section: two eight-bit sub-decoders, three marker values, and a
move-to-front cache, assembled into each outer table. That prose is **identical in the 2006 revision
and in every later one**, so there is nothing further to read.

Twelve distinct structural readings of that composition were implemented and measured against three
real files — per-table against shared sub-trees, tag placement, marker byte order, markers decoded
through the sub-trees against markers read raw, and small bit-offset shifts around the marker and body
boundary. Every one leaves the great majority of the declared tree section unconsumed:

| file | size | tree section unaccounted for |
| --- | ---: | ---: |
| `ajfstr1.smk` | 3 KB | 79% |
| `hypnotix.smk` | 193 KB | 93% |
| `wetlogo.smk` | 724 KB | 98.7% |

A sweep of plus and minus four bits around the marker/body boundary on the worst case found nothing
better than 19 nodes where hundreds are needed. This is not an off-by-something.

## The header's own allocation hints settle it

`MMap_Size`, `MClr_Size`, `Full_Size` and `Type_Size` are stated in the file header, independently of
any parsing hypothesis:

| file | MMap | MClr | Full | Type |
| --- | ---: | ---: | ---: | ---: |
| `ajfstr1.smk` | 2 992 | 400 | 200 | 248 |
| `hypnotix.smk` | 2 272 | 432 | 4 272 | 976 |
| `wetlogo.smk` | 31 232 | 3 048 | 71 512 | 2 512 |

All twelve values divide by eight exactly, implying per-table entry counts from 25 to 8 939. **Every
one of the twelve variants collapses to between one and three leaves per table on every file** —
three to four orders of magnitude short, on a measure that does not depend on any of them being right.

The same measurement isolates the fault. The eight-bit sub-decoders those bodies are built from land
at 100 to 511 leaves on their own, which is the range they should be in. So the defect is not in the
base algorithm, which is confirmed, and not in the sub-decoders, which are plausible on their own
terms. It is in the step that turns two sub-decoders and three markers into the outer table's shape,
and nowhere else.

## The finding worth more than the codec

**A parse succeeding is not evidence here, and a future attempt has to know that before it starts.**

The Tag/Flag/Leaf descent always terminates. Fed arbitrary bits it produces a well-formed tree
regardless of whether those bits were ever a tree. `wetlogo.smk`'s 245 024-bit tree section, parsed as
a single flat eight-bit tree with no low and high split, no markers and no four-table structure at
all — a model that is definitely wrong — terminates cleanly after 2 560 bits and yields a complete
256-leaf tree covering every byte value.

So "it parses without error and the tree looks plausible" carries no information for this format. The
only signals that mean anything are whether a reading consumes close to the declared byte budget, and
whether the pictures it produces match the reference. Twelve hypotheses were tested against the first
of those; none survives it.

This is the strongest instance of a pattern this file records elsewhere — RoQ's published skip reading
reproduces the first two pictures of every file exactly before drifting without bound, and a
row-order-insensitive test fixture once passed against a decoder with its rows upside down. Those are
checks that return success on wrong work. This is a check that cannot return anything else.

## What would change the answer

An independent description of the "Optimized Compression" composition — not the same prose again. RAD
published no format note that has been found. `libsmacker` exists as another implementation but states
no provenance, postdates the reference decoder by seven years and makes no clean-room claim, so it is
not a source this project reads.

Failing that, a harder empirical constraint than the size hints. Those were sufficient to reject all
twelve readings and to localise the fault to one step; they were not sufficient to identify the right
one.

# Electronic Arts TGQ, TQI and MAD, whose only documentation is their own decoder's author

TGQ (`pQTG`/`TGQs`), TQI (`pIQT`) and MAD (`MADk`/`MADm`/`MADe`) are three of the DCT-based codecs in
Electronic Arts' own game-cinematic family — see `EaReader`'s container above, which the CMV and TGV
sections of `README.md` already read two siblings of. All three were investigated together because they
share one wall: the same person wrote every ffmpeg decoder for them and the only detailed technical
description of any of them, and that description says, in the open, that it does not state the one thing
a decoder cannot do without.

## One author, on both sides

`libavcodec/eatgq.c`, `eatqi.c` and `eamad.c` are each headed "Copyright (c) 2007-2008" or "2007-2009,
Peter Ross <pross@xvid.org>" — confirmed directly from ffmpeg's own doxygen pages for each file, not
inferred. The same author wrote `libavcodec/eaidct.c`, documented there as "Electronic Arts TGQ/TQI/MAD
IDCT algorithm" and shared by all three decoders, and `libavformat/electronicarts.c`, the demuxer this
project's own `EaReader` was built independently of.

MultimediaWiki's pages for all three — `Electronic_Arts_TGQ`, `Electronic_Arts_TQI` and
`Electronic_Arts_MAD` — were written by the user `Suxen drol`, confirmed the same person: ffmpeg's own
`libavcodec/mmvideo.c` (the American Laser Games MM decoder, the same era, the same family of game
formats) credits its author as "Peter Ross", contactable at "suxen_drol at hotmail dot com" — the
identical handle, in ffmpeg's own source tree rather than inferred from wiki behaviour alone. `Suxen
drol` is also the primary author of the umbrella `Electronic_Arts_Formats` page describing the shared
chunk structure this project's own container reads.

## What the pages themselves say about where the IDCT came from

This is not merely the same person on both sides — Melanson's Interplay MVE document and this project's
own accepted RoQ and VQA sources are also written by people who went on to implement the same codec
themselves, and that alone does not disqualify a source; what disqualifies these three is that the pages
say outright they do not carry the one algorithm a DCT decoder cannot do without, and point at the
implementation instead of stating it.

The TGQ page's own edit history records it happening: on 7 November 2008, `Suxen drol` edited the page
with the summary "remove EA zigzag reference, add link to FFmpeg IDCT implementation" — the page's own
maintainer replacing whatever it said about the transform with a pointer to his own `eaidct.c`, rather
than writing the transform down. The MAD and TQI pages carry the same gap in the open: both mark their
own bit-packing and IDCT sections with `<FIXME>`, the wiki's own placeholder for "not written yet," and
neither has been filled in since. This is the SVQ1 shape exactly — "the document says so directly rather
than reproducing them," there naming `svq1_cb.h` by filename; here, the page defers to a source file by
the same author without even needing to name it, because editing the page to add the link *was* the
edit.

## Why this settles all three, and why it is not merely "same author"

Every large table this project has accepted from a source shared with an implementation — Melanson's
`interplay-mve.txt`, Kostya Shishkov's VP4 notes, On2's own VP7 document — either predates the
implementation it also produced, states its own method in the open ("you just download original VP3.2
decoder source... and compare it to the structure in `vp4vfw.dll`"), or is a standalone document a
different party's decoder was later written from. None of that is true here for the one piece that
matters: the IDCT is not described, was never filled in across at least seventeen years of the page
existing, and the maintainer's own edit history shows it being replaced with a citation into the
decoder rather than written out. That is a source restating an implementation, not documenting one.

A DCT-based decoder cannot approximate its way past a missing inverse transform the way a container
format can skip an unread field. TGQ, TQI and MAD all reconstruct every block through the same shared
`eaidct.c` routine, so the gap is not one macroblock mode or one escape value among many correctly
described ones — it is the final, load-bearing step of every block in every picture.

No independent source was found either. Electronic Arts published nothing about any of the three;
no patent, academic paper or second reverse-engineering write-up naming its own method turned up in
searching for any of the three by name, their chunk FourCCs, or "EA zigzag" — the one term the TGQ
page's own edit history shows was once on the page and is not anymore.

## What would change the answer

A description of the IDCT — and, for TGQ and TQI specifically, the "EA zigzag" scan order the TGQ page's
own edit history shows was once stated and was removed — from a source that is not `eaidct.c` or a page
that cites it. Failing that, the transform is small enough in principle to recover by the route this
project used for TrueMotion 2's delta table or Escape 124's codebook sizing: a real ffmpeg-decoded
picture's forward transform, checked against the coded coefficients a from-scratch bitstream parser
recovers. That parser was not attempted here, because the container and entropy layers TGQ, TQI and MAD
share with MAD's own MPEG-1-derived run-length coding were not themselves mapped in this pass — the
provenance question was settled first, as it is meant to be, before any of that work was spent.

# Electronic Arts TGV, recovered as far as its own published errors allow

TGV (`kVGT`/`fVGT`) is Electronic Arts' lossless-intra, block-VQ-inter codec, and it clears the bar TGQ,
TQI and MAD do not: `Electronic_Arts_TGV`'s MultimediaWiki page was created on 14 March 2006 and
substantially written by `Suxen drol` on 1 April 2006, a year before `libavcodec/eatgv.c`'s own
"Copyright (c) 2007-2008 Peter Ross" — the direction of dependence is the right way round, the page
predates the decoder it shares an author with — and it prints complete bit patterns for every one of its
five intra-frame compression statements and its inter-frame code book scheme, no `<FIXME>` and no
citation into a decoder anywhere on it. This section is not a refusal: it is the record of how far this
project's own from-scratch decode gets against that page before it stops, kept exactly as TrueMotion 2's
and VP4's own partial sections are, so whoever continues it does not start from the container.

## What the container states, confirmed directly against a real file

`INTEL_S.TGV`, from `samples.ffmpeg.org/game-formats/ea-tgv/`, opens with a `SEAD` audio header and then
a `kVGT` chunk whose own fixed header reads 320 and 200 at the offsets the page states for width and
height — matching `ffprobe`'s own reported picture size exactly — and 256 at the offset the page states
for palette count. The palette itself, read as the plain eight-bit red/green/blue this project's own CMV
investigation above already found the family actually uses rather than the format description's six-bit
or reordered readings, was not separately re-derived here; it is stated the same way and not yet checked
against a decoded TGV picture, since nothing downstream of the intra decompression below yet produces one
to check it with.

Past the palette, a two-byte big-endian `check` field and, when its `0x0100` bit is set — as it is on
this file — a three-byte field before the real one: **the uncompressed buffer size that follows is
exactly 64 000 bytes, 320 times 200 to the byte**, with no rounding and no padding, on the one file this
was measured against. That is a strong, independent confirmation that the header is being read at
exactly the byte offsets the page states, arrived at from the file's own arithmetic rather than assumed.

## The intra compression: one of five statement shapes measured wrong, the rest confirmed

The compressed buffer that follows is a run of variable-length statements, each identified by how many
of its leading bits are set — `111111`, `111`, `110`, `10` or a leading zero — a real prefix code rather
than a fixed opcode byte. Three of the five were confirmed exactly as published, checked byte by byte
against `ffmpeg`'s own decoded first picture:

  - **`111111AA`**, a one-to-three-byte literal run, `size1 = A`, is not exercised early enough in this
    file's own first picture to be independently confirmed past its shape, but never produces a
    disagreement anywhere it appears in the portion measured.
  - **`0CCBBBAA DDDDDDDD`**, a two-byte header naming a short literal run and a back-reference copy, is
    confirmed exactly as published — `size1 = A`, `size2 = B + 3`, `offset = (C<<8) + D + 1` — across
    every occurrence in the 16 845 bytes of this picture that decode correctly, dozens of copies with
    offsets from single digits to several thousand, all landing on already-correct data.
  - **`110CBBAA DDDDDDDD EEEEEEEE FFFFFFFF`**, the four-byte long-offset form, likewise was not seen to
    disagree anywhere it fired within the correctly-decoding portion, though it fires rarely enough in
    this one file that this is a weaker confirmation than the two-byte form's.

**One is measured wrong.** `111AAAAA`, the one-byte "medium literal run" statement, is published as
`size1 = (A + 2) * 4`. Read that way, this file's very first picture desynchronises after twelve
decoded bytes — the ninth pixel of the very first literal run reads a palette index the published
formula's own twelfth byte does not produce, one integer index away from what ffmpeg's decode states,
which is the signature of a run one whole unit too long rather than of a wrong offset or a wrong
palette. Read instead as **`size1 = (A + 1) * 4`** — the constant lowered by exactly one, the same shape
of correction this project's CMV investigation above made to the format's stated palette component
order — the picture decodes correctly for **16 845 of its 64 000 bytes**, more than a quarter of it,
crossing dozens of instances of all five statement shapes with no further disagreement until the point
below.

## Where it stops, precisely

At byte 16 845, the first byte a `10AAAAAA CCBBBBBB DDDDDDDD` statement — the three-byte "long literal,
short copy" form — produces. The published fields for this one — `size1 = C`, `size2 = A + 4`,
`offset = (B<<8) + D + 1` — read from the real bytes at this point (`0x81 0x0A 0xCD`) give `size1 = 0`,
`size2 = 5`, `offset = 2766`, a copy that lands inside data this picture has already correctly decoded
and therefore does not crash — but the five bytes it copies do not match ffmpeg's own decoded picture at
this position, where the four statements above never once produced a wrong byte across many hundreds of
occurrences between them.

The true five-byte run was searched for directly, the way this project's other bitstream recoveries look
for a real match rather than guess at a formula: ffmpeg's own decoded picture states the palette indices
`19, 3, 19, 2, 19` at this position, and that exact five-byte sequence occurs four times in the picture's
own already-correctly-decoded first 16 845 bytes, the nearest at an offset of 3 324 rather than the 2 766
the published formula computes from this statement's own three bytes. **3 324 is not reachable from this
statement's own header bytes by any single-field, single-constant adjustment tried** — not `size2 = A + 3`
or `A + 5` in place of `A + 4`, not `offset` without its `+1`, not the two six-bit fields between the
second and third bytes exchanged, not the second byte's own two-and-six bit split reversed. Every one of
those changes either fails to reach 3 324 from `0x0A`/`0xCD` or reaches it only by also breaking a
different occurrence of the same statement shape that had been decoding correctly up to this point.

That last part is what turns this from "one more constant to find" into a real stopping point: unlike
the `111AAAAA` correction above, which improved every occurrence of that statement it touched, no
single reinterpretation of `10AAAAAA CCBBBBBB DDDDDDDD` tried here fixes this occurrence without also
un-fixing earlier ones this project has independent evidence are already being decoded to the byte
correctly against ffmpeg's own picture.

Nothing beyond the intra picture was attempted — the inter-frame code book scheme (`fVGT`), covering the
overwhelming majority of a real file's pictures, needs an already-correct intra reference to measure
against and was not reached.

## What is already solved, if this is picked up again

The container and picture header — including the exact 64 000-byte uncompressed size, confirmed to the
byte against the file's own arithmetic — the two-byte and four-byte statement forms, and the corrected
one-byte literal-run formula are all checked against real bytes and need no revisiting. What remains is
the three-byte statement's own bit assignment, and, once a picture decodes whole, the `fVGT` code book
scheme this investigation did not reach at all.

## What would change the answer

A description of the three-byte statement's own bit layout from a source that states it rather than
paraphrases the same wiki page this investigation already used — or a second real TGV file whose own
early pictures exercise this statement enough times, at small enough offsets, to narrow a reinterpretation
by the same kind of cross-checking that settled the one-byte statement above. The one file this was
measured against, `INTEL_S.TGV`, uses the two-byte statement far more often than the three-byte one in
its first picture, which is why the two-byte form is confirmed across dozens of instances and the
three-byte form's true bit layout is not yet pinned down by even a second occurrence to compare against.

# Deluxe Paint Animation (ANM), where the container is Electronic Arts' own documentation and the
codec is only their source code

ANM (`anm`, FourCC-less, magic `LPF `) was investigated because it looked like the cheapest of the
remaining game formats on the same promise several already-decoded ones had: a container recovered from
prose, a small alphabet of frame-to-frame operations, and real samples to check either against. The
container half of that promise holds completely. The codec half does not, and the reason is specific
rather than a shortage of searching.

## What was recovered, and verified against real files

Electronic Arts released a "Programmer's Kit for DeluxePaint Animation" — an executable, C source and a
plain-text file, `ANIMFILE.TXT`, whose own cover letter (`READFRST.TXT`) says outright: "For information
on the ANM (animation) file format, please see the comments in the LPFILE.C file." `ANIMFILE.TXT` is
that description extracted into prose on its own, and it is a genuinely independent, first-party
document — Electronic Arts describing a format Electronic Arts designed, years before any third-party
decoder existed. MultimediaWiki's own DeluxePaint Animation page states plainly that its "Format
description" section is "recovered from `http://www.whisqu.se/per/docs/iffanim.txt`, which is now dead,"
and a byte-for-byte comparison shows the wiki page and the kit's own `ANIMFILE.TXT` are the same text —
so the wiki page's provenance is not somebody's decoder, it is this same first-party document at one
remove.

That document gives, in full, the whole container: a 2816-byte header (`LPF ` magic, large-page and
record limits, a `contentType` of `ANIM `, picture size, an EA-defined `CompressionType` of 1 named
"RunSkipDump", frame count and rate), an inline 256-entry RGBX palette, a 256-entry large-page directory
copied from the pages themselves, and the "large pages" proper — up to 64KiB blocks, stored in whatever
order the encoder chose rather than playback order, each opening with a small header and a table of its
own records' lengths, each record itself opening with a byte stated as "always 66" and a flags byte.

All of it was checked against three real files from `samples.ffmpeg.org/game-formats/anm/` —
`CINEOV2.ANM`, `INTRO1.ANM` and `SW.ANM`, 158, 156 and 44 records respectively. Every large-page
directory entry's record count sums to the file's own stated `nRecords`; every non-empty record's first
byte is 66 with no exception across 320 records; and `CINEOV2.ANM`'s 158 records include exactly five of
zero length, leaving 153 — which is exactly what `ffprobe -count_frames` reports for the file, a
zero-length record being "no change from the frame before it" rather than a frame of its own, confirmed
independently by the two counts agreeing.

## Where it stops

`ANIMFILE.TXT` stops exactly at the record header. What a record's compressed bytes mean — the
"RunSkipDump" scheme the header field names but the prose never explains — exists nowhere except inside
the Programmer's Kit's own reference source, `LPFILE.C` and `ANIMIO.C`, an assembly-optimised C
implementation with named routines for what its own comments call short and long forms of a skip, a dump
and a run. That source is exactly the kind of material this project does not transcribe: not a
third-party reverse-engineer's notes this time, but the format owner's own implementation all the same,
and the licence terms `READFRST.TXT` states — provided to registered users of a 1990 consumer product,
not published under any open licence — give no more standing to copy from it than ffmpeg's own decoder
would. A second, wider search turned up nothing else: no independent write-up states the opcode byte
values, the short/long thresholds or the bit layout distinguishing the three operation kinds: everything
found either restates the container fields above or points at an implementation — this project's own or
ffmpeg's — for the rest.

Blind measurement was tried before this was set aside. Every non-empty record examined opens with the
same four bytes — `42 00 01 00` — read as the documented header (`IDnum` 66, `Flags` 0) followed by two
bytes with no stated meaning once `Flags` is zero; and the byte immediately after that pair is `0x80` in
every record checked, in three unrelated files, at every position a large page places one. A single
opcode byte recurring at the very start of unrelated compressed streams this consistently is a real
finding — most plausibly the compressor always opens a record with the same class of instruction, a
"dump" large enough to need its long form after a scene change — but it names one byte's likely class and
nothing about where that class's own count field starts or ends, which byte values the other two classes
of instruction use, or which of them switches between an eight-bit and a sixteen-bit count. Recovering
that would need the same kind of exhaustive, position-by-position bisection against the oracle that
recovered TrueMotion 2's block-type allocation or Escape 124's codebook sizing, and this investigation
did not carry it that far.

## What would change the answer

A description of RunSkipDump's opcode bytes and count-field widths from a source that is not an
implementation — a second archived copy of a technical document that goes further than `ANIMFILE.TXT`
does, or a published reverse-engineering account that states its own method rather than restating
`LPFILE.C`. Failing that, the blind measurement above, carried to the same depth this project reached for
TrueMotion 2: enough records at enough known byte offsets, compared pixel by pixel against ffmpeg's own
decode of the same file, to pin down each opcode class's byte values and count-field width one at a time
rather than inferring the first byte's class alone. The container facts above — the header, the palette,
the large-page directory, and the record framing, all confirmed against three real files — are recorded
here so that whoever picks this up again starts from the record's own compressed bytes and not from the
file format around them.

# Chronomaster DFA, whose page is the cleanest case of a paraphrase this project has found without a
citation to prove it

DFA (`dfa`, magic `DFIA`) was investigated as the next Amiga-and-legacy-adjacent item on the list —
DreamForge's FMV format for *Chronomaster* and *Anvil of Dawn* — and it stops before a byte of it was
written, on provenance alone.

## What MultimediaWiki's page reads like

The page states the 128-byte header and the general chunk preamble in the same plain, field-by-field
prose every other container on this project's list was read from, and that half is unremarkable. What
follows it is not: a full section per video chunk type — `TSW1`, `BDLT`, `WDLT`, `TDLT`, `DSW1`, `DDS1`
— each given not as a description of what the coding achieves but as a block of C-shaped pseudocode,
complete with `get_le16()`, `get_byte()`, `memmove()`, `cur_frame_pos`, `frame_ptr`, `line_ptr`, and a
`while (segments--)` idiom repeated across three of the six sections. Bit-level tie-breaks that a
genuine reverse-engineer would ordinarily explain — why `DDS1`'s mask advances by two rather than one,
why `WDLT`'s stripe count is read as a signed value and re-read when its top bits are set — are stated
as bare code with no explanation of how they were determined, which is what a working implementation's
own logic looks like once its comments are stripped, not what a decoder recovered by measurement reads
like.

That is exactly the shape this project has already learned to distrust for a specific, checkable
reason, not a stylistic complaint. MSS1 and MSS2's own sections above turned on function names lining up
with `libavcodec/mss2.c` one for one, typo included; SpeedHQ's on a table that is a verbatim copy of
`libavcodec/speedhq.c`'s own arrays. DFA's page offers no such single fingerprint to point at, but it
offers something just as telling by its absence: unlike CDXL's, BFI's and IFF ANIM's own MultimediaWiki
pages — all read for this same family of formats, all citing a named first-party source or plainly
marking what is not known — DFA's page cites nothing at all for any of its six chunk algorithms. No
README, no archived technical document, no reverse-engineer credited by name. A page built from real
reverse-engineering ordinarily says so, the way this project's own README does for VP3's frame header or
TrueMotion 2's tables; a page built by transcribing a working decoder's source into prose has nothing to
cite, because the source was the decoder.

`ffmpeg` carries a real `dfa` decoder, `libavcodec/dfa.c`, so the paraphrase this page most plausibly
descends from already exists in the one place this project does not read. Two accompanying sample notes
on `samples.ffmpeg.org` — `chronomaster-dfa.txt` and `LOGOS.DFA.TXT` — were checked for a second,
independent source; the first names only the game the samples come from, and the second names this same
wiki page as its own reference, which settles nothing either way but adds no independent confirmation.

## Why this stops the investigation rather than starting it

Six chunk types, each its own coding scheme, is a large surface to get right by measurement alone, and
this project's own standard for a codec like this is exact equality on every frame — the same bar CDXL,
BFI and IFF ANIM all cleared in the same investigation pass DFA was reached in. Attempting it from a
page this project already has good reason to believe is a paraphrase would mean one of two outcomes:
either the transcription happens to be faithful and the result is, in substance, a translation of
`libavcodec/dfa.c` with the serial numbers filed off — exactly what "this project does not transcribe
implementations" exists to prevent, whether or not a single line of code is copied — or it is not
faithful, and a subtle mistranscription somewhere in six separate decoding loops produces a picture that
is wrong in a way nothing here would have reason to doubt, since the source it was checked against was
the same paraphrase. Neither outcome is worth having.

## What would change the answer

A description of `TSW1`, `BDLT`, `WDLT`, `TDLT`, `DSW1` and `DDS1`'s coding from a source that names
itself and how it reached its conclusions — a DreamForge document of the kind Electronic Arts published
for Deluxe Paint Animation's container (see that section above), or a reverse-engineer's own account
that says, plainly, that the six chunk types were worked out from files and not read out of a decoder.
Failing that, this project's own blind measurement against real files — `0000.dfa`, `0001.dfa`,
`0002.dfa` and `LOGOS.DFA` are all on `samples.ffmpeg.org` and ffmpeg decodes all four, so a genuine
from-scratch derivation has an oracle to check itself against — carried far enough to recover all six
schemes independently, the same kind of effort this project spent on TrueMotion 2's tables and BFI's own
back-reference and fill codes. Neither was attempted here because the page's own shape settled the
question before either was needed.

# 8088flex TMV, which has neither a description nor a whole sample

TMV (magic `TMAV`) was investigated next and stops earliest of anything in this file: it clears none of
the three things every other entry here had at least one of — a published document, a paraphrase-shaped
wiki page to rule out, or a real, complete sample corpus.

MultimediaWiki carries no page for it at all, under either name tried — `TMV` and `8088flex_TMV` both
return "There is currently no text in this page." `samples.ffmpeg.org` carries no directory for it
either, under `game-formats/`, `V-codecs/`, or the root. FFmpeg's own separate FATE test-suite server,
`fate-suite.ffmpeg.org/tmv/`, does carry one file, `pop-partial.tmv` — and its own name says what it is:
ffmpeg's decoder itself logs "Input buffer too small, truncated sample?" while reading it, and stops 110
pictures into a stream its own header states holds 111. One partial file is what exists to check anything
against, where every other entry in this file had at least a handful of complete ones.

## What little the header gives up

The twelve bytes the file opens with cross-check cleanly against what ffprobe reports for the same file,
which is the only reason this section exists at all rather than nothing: bytes 4-5, read little-endian,
are 22058 — ffprobe's own audio sample rate for the file, exactly. Bytes 6-7 are 368, which is exactly
`22058 * 184 / 11029` — 184/11029 being the video stream's own time base, so 368 is the count of 8-bit
mono audio bytes one video frame's worth of time actually holds, derived two different ways and landing
on the same integer. Byte 9 is 40 and byte 10 is 25 — 320/8 and 200/8, the file's own picture size, which
ffprobe also confirms directly. None of this reaches the picture itself: what comes after byte 12 is
opaque. It is not a plausible small frame-length prefix (32689 as a two-byte count, or a nonsense
four-byte one), and the string `TMAV` does not recur anywhere later in the file to mark where a second
frame might begin, so even the per-frame chunk boundary was not located, let alone the coding inside it.

## Why this was not carried further

Frame one's own decoded picture, from ffmpeg, uses exactly four colours — 0, 85, 170 and 255, evenly
spaced — which is the shape a 2-bit index widened by bit-replication produces and is consistent with an
intro card meant to look right in a very restricted mode. But the header bytes immediately following the
twelve confirmed above do not read as that image under any of the readings tried — not as a stored
palette, not as packed 2-bit pixels, not as a plausible run-length opcode stream — and with only one
sample, and that one truncated before its own last frame, there is no second file to weigh a candidate
reading against the way `INTEL_S.TGV`'s repetition settled TGV's own two-byte statement above. Blind
reverse-engineering of an unknown format from a single incomplete instance is not a sound way to reach
this project's own bar of exact equality against every frame of every file; it was not attempted past the
header fields recorded here.

## What would change the answer

A second real TMV file, complete rather than truncated, from any source — the `8088 Corruption` /
`8088flex` project this format's name points to was searched for (its likely home under
`trixter.oldskool.org` and a GitHub search for `8088flex`/`TMV`) and turned up no released source, README,
or format note describing the picture coding. Either that documentation surfacing, or enough additional
real files to let the same kind of position-by-position bisection against ffmpeg's decode that recovered
CDXL's and BFI's own byte layouts run in earnest, would change the answer. The header fields confirmed
above are recorded so whoever finds either does not have to re-derive them.

# LOCO, whose only description says on its own face where it came from

LOCO (`LOCO`) was investigated as part of the lossless group that produced Ut Video, MagicYUV, ZeroCodec,
LCL ZLIB and Creative YUV. It is a prediction-and-entropy codec of the HuffYUV family and it looks, at
first glance, exactly like the kind of thing that family's other members turned out to be: a
MultimediaWiki page with a real technical description on it, enough to build from.

It stops on provenance, and the evidence is unusually clean because the page itself states it.

## The direction of dependence, from the page's own words and its own history

MultimediaWiki's LOCO page opens with the sentence *"This page is originally based on a description
written by User:Kostya."* That user's own wiki biography describes him as a reverse engineer, and he is
Konstantin Shishkov — a prolific author of exactly this class of decoder.

The page's edit history settles the rest. It was created at 12:03 on 5 February 2006 by "Multimedia
Mike", carrying the whole 2,290-byte technical write-up in a single edit, and Kostya himself edited it
the same day. FFmpeg's own `loco.c` was added on 1 March 2005 — eleven months **earlier** — in a commit
whose message reads "go LOCO, courtesy of Kostya Shishkov". So the decoder came first, its originating
author is the same person the page credits its description to, and the description arrived the better
part of a year afterwards.

That is both disqualifying legs of the test this project already applied to TSCC2, Go2Meeting, MSS1,
MSS2 and Electronic Arts TGQ at once, and it is a cleaner case than any of them: those had to be
established by matching function names or by reading an edit history against a commit date, where this
page volunteers its own source in its first line. The page is the decoder described after the fact, not
a specification the decoder was built from — the opposite of ASV1 and ASV2's `asv1.txt`, which is
written as a specification, carries a changelog naming two authors, and predates any implementation of
it in this repository by years.

## Why nothing else fills the gap

No other description of LOCO's bitstream was found. It is not a vendor format with a published
standard behind it, there is no encoder in ffmpeg to drive a corpus with, and the only technical text
in existence is the page above. Blind recovery from files is not a route here either: the codec is
prediction plus an entropy coder whose parameters are exactly what the page states and nothing else
does, so there is no independently-sourced anchor of the kind Indeo 3's header offsets or TrueMotion
2's own container framing gave those investigations to start from.

## What would change the answer

A description of LOCO's prediction and entropy coding from a source that is not an implementation and
not a retrospective account of one — the format's own author, a vendor document, or a reverse
engineer's write-up that predates the decoder and says plainly how it was produced and from what.
Nothing found is that, and the page that exists says in its own first line that it is not.

# Canopus Lossless (CLLC), where the page never had anything on it to check

Canopus Lossless (`CLLC`) was investigated with LOCO and VBLE as part of the lossless group. Unlike
those two it needs no argument about who wrote what: there has never been a technical description of
this format anywhere to argue about.

## What the page is, and has always been

MultimediaWiki's `CLLC` redirects to "Canopus Lossless", and that page's entire content is three
bullet points — the four-character code, the company, and a link to a proprietary binary installer.
There is no header layout, no coder, no prediction rule, not one bitstream fact.

Its full edit history, six revisions, shows it was never anything else. It was created at 00:50 on 20
January 2009 by "Nazo" at 127 bytes; Nazo and "Multimedia Mike" edited it through August 2009; on 15
October 2012 a user removed content with the edit summary *"no more undiscovered, available in
ffmpeg/libav"* — that is, the page's own note that the codec was unimplemented was stripped once a
decoder existed elsewhere, not replaced with a description of it; and the last edit, in May 2013,
added the link to the vendor binary. FFmpeg's own `cllc.c` was added on 27 July 2012, which is what
that October 2012 edit is reacting to.

So the sequence is the reverse of a specification being written and then implemented. Nothing was ever
written down; a decoder appeared; the page was updated to stop calling the codec undiscovered.

Canopus — later Thomson, then Grass Valley — published nothing about it either. That is the same
finding this file already records for Canopus HQ, HQA and HQX: the vendor's own material states no
bitstream fact at all. No academic citation exists, and there is no standard behind it.

## What the corpus does say, and why it does not rescue this

Three real recordings exist on ffmpeg's own test-suite server at `fate-suite.ffmpeg.org/cllc/`, and
they cover three different colour arrangements — `sample-cllc-rgb.avi` at 640x480 (`rgb24`, 1000
pictures), `sample-cllc-argb.avi` at 1280x720 (`argb`, 19 pictures) and
`sample-cllc-yuy2-noblock.avi` at 640x480 (`yuv422p`, 101 pictures). ffmpeg decodes all three without
error, so an oracle is available, and the framing is visibly chunked: every packet measured opens with
the four bytes `INFO` and a 24-byte length, in both the RGB and the ARGB file alike.

**The small-frame argument that settles Indeo and TrueMotion 1 does not apply here, and this entry does
not pretend it does.** The ARGB file's smallest picture is 338,464 bytes against a 1280x720 raw frame
of 3,686,400, so there is easily room in every frame for whatever tables the coder needs; this format
may well be self-describing the way TrueMotion 2 turned out to be.

What stops it is the other wall, the one MSS1 and MSS2 stop at: there is nothing independent to build
from at all. The only two descriptions of this bitstream in existence are implementations — ffmpeg's
decoder and Canopus's own binary — and this project does not transcribe either, whether the author is a
third party or the format's own vendor. Blind recovery of an unpublished Huffman coder and its
prediction from three files, to this project's standard of exact equality on every sample of every
frame, was not undertaken and is not claimed; recording it as attempted-and-failed would be as
dishonest as recording it as done.

## What would change the answer

A description of CLLC's frame layout, entropy coding and prediction from a source that is not an
implementation — Canopus, Thomson or Grass Valley documentation, or an independent reverse-engineering
write-up that states how it was produced and from what. The corpus above and the `INFO` chunk framing
are recorded so that whoever finds one does not start from nothing.

# Matrox Uncompressed SD (M101), which has no description, no sample and no encoder

M101 was investigated with the lossless group and is the one member of it that stops for the plainest
reason available: there is nothing to read and nothing to read it against. It joins 8088flex TMV as an
entry that clears none of the three things every other codec in this file had at least one of — a
published document, a paraphrase-shaped page to rule out, or a real sample corpus — and it is in a worse
position than TMV, which at least had one truncated file.

## It is a packing rather than a coding, which is what makes the absence decisive

M101 is Matrox's uncompressed standard-definition format, one of a family with M102 for high definition
and M103 carrying alpha, in 8-bit and 10-bit variants. Uncompressed means there is no entropy coder to
recover and no tables to find — the entire format *is* a byte layout. That would normally make it one of
the cheapest things in this package to do: `v210`, `r210`, `r10k`, `y41p` and `012v` are all exactly
this shape, and every one of them was recovered by sweeping candidate readings against ffmpeg fed known
or pseudo-random samples until one matched.

That method is the whole of how a packing gets recovered here, and it needs one of two things: an
encoder to feed known content through, or a real file to sweep against. M101 has neither, and that is
the finding.

## What was checked, and came back empty

  - **MultimediaWiki carries no page.** `wiki.multimedia.cx/index.php/M101` returns 404, and the wiki's
    own full-text search for "M101" answers "There were no results". There is not even a stub to
    evaluate the provenance of, which is a different situation from Canopus Lossless's three bullet
    points and from every screen-capture codec in this file.
  - **There is no encoder.** `ffmpeg -encoders` lists nothing for `m101`, `m102` or Matrox at all; the
    codec is decode-only, so no corpus can be built to order the way v210's and y41p's were.
  - **There is no sample anywhere searched.** Neither `samples.ffmpeg.org/V-codecs/` nor ffmpeg's own
    `fate-suite.ffmpeg.org` carries an `m101`, `m102` or Matrox directory, and fourcc.org's registry
    has no entry either. Matrox itself publishes no format documentation for it.

## The one thing the record does say, and it cuts the wrong way

The decoder's own commit history carries the note *"TODO: find out which LSB for 10bit go where"* —
its author's own statement that the 10-bit sample layout was not settled when it was written. So even
setting aside that this project does not read that source, the one implementation that exists records
uncertainty about the exact question a bit-exact decoder would have to answer. There is no second
decoder to weigh it against and no file to test either reading on.

## What would change the answer

A single real M101, M102 or M103 file from Matrox hardware, or Matrox's own documentation of the pixel
packing. Either one alone would probably be enough, because an uncompressed layout has no hidden
tables: with a file, the sweep that recovered r210's and y41p's layouts runs directly; with the
documentation, there is nothing else to derive.

# Dxtory, whose description stops exactly where the compression starts

Dxtory (`xtor`) is the capture codec of the Windows screen-recording tool of the same name, written for
recording high frame-rate games. It was investigated with the lossless group, and it stops twice over:
the description that exists does not describe the compression at all, and what description there is
comes from the decoder's own author on the day he wrote it.

## The page does not reach the coding

MultimediaWiki's Dxtory page is five lines, and this is the whole of its technical content:

> Frame data consists of 16-byte header and YV12 blocks (2x2 block of luma and two bytes for chroma).

That is a statement about what the picture is made of, not about how it is coded. Dxtory compresses —
that is the entire point of a capture codec — and there is not one word here about the entropy coding,
the prediction, the block ordering, the meaning of any of the sixteen header bytes, or how a frame's
coded bytes turn into those YV12 blocks. Read at face value and believed entirely, this page does not
get a decoder to its first sample.

That is a different failure from the rest of this file. LOCO's page and TSCC2's are detailed enough to
build from and are disqualified for where they came from; this one would not be enough even if its
provenance were spotless.

## And its provenance is not spotless

The page has exactly one revision in its entire history: 03:24 on 9 December 2011, by User:Kostya —
Konstantin Shishkov. FFmpeg's own `dxtory.c` was committed by Kostya Shishkov at 10:06 UTC **the same
day**, under the message "Dxtory capture format decoder", roughly seven hours later.

The page therefore predates the commit by hours rather than following it, which is the opposite of
LOCO's ordering. But same author and same working day is not an independent description that a decoder
was later built from; it is one person's notes from the session that produced the decoder, published
alongside it. This project already declined TSCC2 and Go2Meeting on the finding that each write-up's
author is on record as the implementation's author too, and this is that pattern with the interval
compressed to a single morning.

Taken together the two findings settle it: the only account of this bitstream that is not an
implementation does not describe the compression, and the person who wrote it wrote the implementation
the same day. Everything a decoder actually needs exists only in ffmpeg's decoder and in Dxtory's own
binary, and this project transcribes neither.

## What the corpus is

One real file, `dxtory_mic.avi`, on ffmpeg's own test-suite server at `fate-suite.ffmpeg.org/dxtory/`.
There is no ffmpeg encoder, so no corpus can be built to order and no known content can be driven
through the coder to calibrate a candidate reading. One uncontrolled recording is the same thin base
that stopped MWSC and ScreenPressor here, and it is thinner still against an entropy coder nothing
describes.

Dxtory's own vendor site publishes no bitstream documentation.

## What would change the answer

A description of the actual compression — the entropy coding, the prediction and the sixteen header
bytes — from a source that is not an implementation, or from the vendor. Failing that, an encoder or a
larger corpus of real files would at least make the blind route conceivable; with one file and no way
to choose what goes into it, it is not.

# VBLE, whose one page's own "documentation" is a line of C

VBLE (`VBLE`) was investigated with LOCO and Canopus Lossless as the third member of the same lossless
group, and it fails the provenance test the other two are settled by, on evidence more direct than
either: its one technical source does not merely trace back to an implementation, it quotes one.

## What the page says, in full

MultimediaWiki's `VBLE` page opens by naming its subject — "a lossless codec for YUV colourspace
written by a person known as Mark FD" — states that it "employs standard median prediction and coding
components with reduced number of bits," describes the frame as a luma line followed by two chroma
lines coded as pixel quads, and gives exactly one further fact: a formula for widening a coded value
back to a sample, printed not as a rule in words but as a literal C conditional expression, `pix & 1 ?
255 - (pix >> 1) : (pix >> 1)`. That is the whole of the page. There is no header layout beyond the one
line above, no entropy coder named, no bit order, nothing about the prediction beyond calling it
"standard."

## Who wrote it, and when

The page has one revision. It was created in full — all 766 bytes of it — on 9 November 2011 by the
user "Kostya," the same Konstantin Shishkov whose own wiki biography already settles LOCO's entry above:
a reverse engineer and a prolific author of decoders for exactly this class of obscure format. Nobody
else has ever edited the page.

FFmpeg's own `vble.c` was not written by Kostya — its first commit, "VBLE Decoder," is Derek Buitenhuis's,
landed 11 November 2011, two days after the wiki page. That puts this entry on the opposite side of
LOCO's chronology: LOCO's decoder came eleven months before its page, and this page came two days
before its decoder. The direction-of-dependence test LOCO settles on a commit date does not, by itself,
settle this one the same way.

It does not need to. A specification does not carry a line of C. `pix & 1 ? 255 - (pix >> 1) : (pix >>
1)` is not prose describing a rule, a table, or a worked example the way Niedermayer's `asv1.txt` gives
ASV1 and ASV2's own tables in full — it is an expression, complete with its own operator precedence and
a ternary a reader has to already know C to parse, exactly the shape a decompiler or a disassembler
hands back and exactly unlike anything a person explaining a format to someone else would choose to
write instead. Whichever binary it came out of — Mark FD's own original encoder/decoder, most plausibly,
given the two-day gap before ffmpeg's own decoder existed to quote — the page is a transcription of
running code, not a description of one, which is the same failure this project already declines LOCO,
TSCC2, Go2Meeting, MSS1 and MSS2 for, reached here by the content itself rather than by a citation or a
matching function name.

## What was not found to fill the gap

No vendor document from Mark FD or anyone else describing VBLE was found. FFmpeg carries no `vble`
encoder, so no corpus can be built to order; one real file exists, `fate-suite.ffmpeg.org/vble/
flowers-partial-2MB.avi` — named, and sized, as a deliberately incomplete capture rather than a full
recording — and it was not carried further, because a corpus is not what this format is missing.

## What would change the answer

A description of VBLE's prediction and entropy coding, or of the widening formula above, written in
prose or tables rather than transcribed from a binary — from Mark FD himself, from a document that
names him as its source, or from a reverse-engineering write-up that says plainly how it was produced
and from what, the way this project would need for LOCO or Canopus Lossless above.

# HuffYUV MT (HYMT), where the only thing missing is the only thing not written down

HYMT is a community fork of Ben Rudiak-Gould's HuffYUV, made multithreadable. This package already
decodes HuffYUV and FFVHUFF exactly — the median, left and gradient predictors, the Huffman tables in
the stream description, the word-swapped bit order, the longest-length-down code assignment, all of it
— so on the face of it HYMT should be the cheapest entry this project could add: reuse everything and
handle whatever the fork changed.

The trouble is that what the fork changed is precisely and exclusively the part nobody wrote down.

## What is published, in full

MultimediaWiki's "Huffyuv mt" page, created on 2 May 2009 by "Nazo" in two edits, is this in its
entirety, technical content included:

> This codec is multithreadable codec based on HuffYUV. This codec is compatible with Huffyuv in
> non-multithread mode.

plus the four-character code `HYMT`, a link to the author's site, and — nine years after it was written
and six after ffmpeg gained a decoder — the page is still filed under `Category:Undiscovered Video
Codecs`. There is no header layout, no table format, no slice structure and no bit order. The main
HuffYUV page does not mention HYMT or the multithreaded mode at all.

The first-party source is no better. The author's own project page carries a download and a version
changelog: it confirms the codec is a fork of HuffYUV v2.1.1 and that **v613 is where the four-character
code became `HYMT`**, and its remaining entries are "decoding fixes", "compress function corrections",
a Japanese resource file and a 64-bit installer. Not one line describes a format change.

## Where the difference actually lives

The fork's own claim — compatible with HuffYUV in non-multithread mode — says by implication that a
file written in multithread mode is *not* classic HuffYUV, and that is the whole of what is known about
how the two differ from any published source. The shape of the difference is a slice table for thread
partitioning: some per-slice offsets, sizes and a slice height, so that threads can start at several
points in a frame at once.

Knowing the shape is not knowing the encoding. Where that table sits, how many entries it has, how wide
its fields are, in what byte order, whether the predictors and the Huffman state reset at each slice
boundary or run through it, and how it interacts with the header forms HuffYUV already has — every one
of those is a decision a bit-exact decoder must get right, and not one of them is published anywhere.

## The one complete description is a GPL implementation

The wiki page links the fork's own GPL v2-or-later decoder and encoder source, which does describe the
format completely, being it. This project does not transcribe implementations, and this one carries a
second problem on top of that rule: a GPL v2+ source translated into an LGPL-3.0-or-later library is a
licence incompatibility as well as a provenance one. ffmpeg's own HYMT decoder, added by a different
author in 2018, is the other implementation and is barred for the usual reason.

## And there is no file to work from

No HYMT sample turned up anywhere searched. `fate-suite.ffmpeg.org/hymt/` returns 404 and there is no
FATE test for the codec; `samples.ffmpeg.org/V-codecs/HuffYUV/` holds twenty files and every one of them
is classic `HFYU`. ffmpeg has `huffyuv` and `ffvhuff` encoders but none for HYMT, so no corpus can be
built to order either. Running the fork's own Windows binary would produce files, and would still leave
the slice table undocumented — it would supply the corpus and none of the description.

## What would change the answer

A statement of the multithreaded frame layout — where the slice table sits, its field widths and order,
and whether prediction and the Huffman state carry across a slice boundary — from a source that is not
an implementation. That is a small document, and everything around it is already decoded in this
package, which is what makes this entry a genuinely narrow miss rather than a wall.

# MidiVid Archive (MVHA), a header sketch written the day after the decoder

MVHA is the lossless member of the MidiVid family, the codecs behind a run of early-2000s console game
cinematics. It was investigated with the lossless group and stops for two reasons, either of which
would be enough on its own.

## The description arrives after the decoder

The only account of this bitstream anywhere is the "MidiVid Archival" section of MultimediaWiki's
Midivid page, added on 26 November 2019. FFmpeg's own `mvha.c` — "avcodec: add mvha video decoder", by
Paul B Mahol — carries an author date of **25 November 2019, 11:59:56 UTC**, the day before, with its
commit landing on the 27th.

So a working decoder existed before the description of the format did. That is the direction of
dependence this project treats as disqualifying, and while the interval here is a day rather than the
eleven months LOCO's page trails its decoder by, the order is the same one and the conclusion does not
change with the size of the gap. Nothing found suggests an independent reverse-engineering write-up
that predates either: the codec author's own survey of the MidiVid family, published two months
earlier in September 2019, covers MidiVid, MidiVid Lossless and MidiVid 3 and does not mention the
archival codec at all.

## And the description does not reach a decoder anyway

This is the whole of it. A frame is four bytes of compression type — `HUFY` for Huffman coding, `LZVY`
for deflate — then four bytes of source size, then data. For `LZVY` the data is a deflate stream. For
`HUFY` it is three bytes of decompressed size, one byte of start symbol, one byte of symbol count less
one, then tree weights, which "can be zero for non-present symbols and coded as either `1` plus 12 bits
or `0` plus 3 bits". The decompressed result is YUV420, Y plane then U then V, and the codec is
described in one line as "Huffman coding or deflate plus median prediction".

Every remaining question is one a bit-exact decoder has to answer, and none is answered:

  - how a tree is built from the weights — the ordering, the tie-breaking, the code assignment;
  - the bit reader's endianness and fill order, which for this family of codecs is exactly the sort of
    thing that differs per codec and is never guessable — HuffYUV's own bits arrive in byte-swapped
    little-endian words, and its codes are handed out from the longest length down rather than the
    shortest up;
  - what the twelve-bit and three-bit weight forms mean numerically;
  - how the median prediction is seeded, per row and per plane;
  - the plane strides and the chroma dimensions;
  - whether inter frames exist at all.

The `LZVY` half is genuinely readable — deflate is fully specified and this package already inflates
LCL ZLIB and ZeroCodec — but a decoder that reads one of a format's two compression types is not a
decoder for the format, and the split between them is not something a file gets to choose on a
reader's behalf.

## And there is no file to check against

No MVHA sample turned up anywhere searched. `samples.ffmpeg.org/V-codecs/` carries `MVDV.avi`, `MVDV/`,
`MV43/`, `MVI2/`, `MVLZ.avi` and `mv30.avi` — the rest of the family — and nothing for MVHA;
`fate-suite.ffmpeg.org/mvha/` and `/midivid/` both return 404, and there is no FATE test. ffmpeg has no
MVHA encoder either, so a corpus cannot be built to order.

## What would change the answer

A statement of the Huffman tree construction, the bit order and the prediction seeding from a source
that is not an implementation, together with at least one real file to measure against. The header
sketch above is reproduced here so that whoever finds either does not have to locate it again.
