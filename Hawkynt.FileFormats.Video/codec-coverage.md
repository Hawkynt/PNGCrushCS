# What is covered, against the whole of libavcodec

This package is measured against ffmpeg's decoder list because that list is the closest thing the
field has to a census of video codecs. This file records how much of it is reached, how the number is
arrived at, and what is left — so that "how far along is this?" has an answer that is not a guess.

## The denominator

`ffmpeg -decoders` reports **305 video decoders**. That is not 305 formats, and using it as the
target would flatter the result. Four groups are removed before counting:

| Removed | Count | Why |
| --- | --- | --- |
| Hardware and wrapper backends | 32 | `h264_cuvid`, `hevc_qsv`, `vp9_v4l2m2m` and the rest decode a codec already in the list. A second backend for H.264 is not a second format. |
| External library wrappers | 7 | `libdav1d`, `libaom-av1`, `libvpx`, `libjxl` and friends duplicate a native decoder or belong to the image package. |
| Still-image codecs | 51 | PNG, JPEG, TIFF, WebP, PCX, Targa, EXR, DDS and the rest are `Hawkynt.FileFormats.Images`, which reads far more formats than ffmpeg does. |
| Raw and null | 4 | `rawvideo`, `bitpacked`, `vnull`, `wrapped_avframe` are not coded formats. |

That leaves **211 distinct video codecs**, which is the number this package is measured against.

## Where it stands

| | Count | Share |
| --- | --- | --- |
| Decoded and verified against ffmpeg | 55 | 26% |
| Established as not implementable from files alone | 34 | 16% |
| Not yet attempted | 122 | 58% |

The 55 are the codec table in `README.md`, counted as distinct libavcodec decoders rather than as
table rows — one row covers several names where a decoder does. Every one was cross-checked frame by
frame against ffmpeg's decode of the same bitstream before it was merged, and the measurements are in
each one's section of that file. The ones that reach exact equality on every sample of every frame
are Microsoft RLE, Microsoft Video 1, Cinepak, QuickTime Animation, Apple Video, Apple Graphics, FLIC, HuffYUV,
FFVHUFF, FFV1, ZMBV, TSCC, CSCD, Flash Screen Video, Flash Screen Video 2, id RoQ, Interplay Video,
id Cinematic Video, Westwood VQA Video, Electronic Arts CMV, Commodore CDXL Video, IFF ANIM Video, BFI Video, QPEG Video,
Ut Video, MagicYUV, v210, r210, r10k,
y41p, CLJR, ZeroCodec, LCL ZLIB
and Hap — the two colour-space ones over 883 and 1,446 frames, in all six and all seven of their
colour spaces, and the packed layouts on the sample data itself, at its own coded depth and with
no display conversion in the way, over 120, 90, 90, 90 and 60 frames at three geometries each.
CLJR is the one lossy format among them — measured against ffmpeg's own decode rather than the
source, because its encoder dithers. ZeroCodec is measured the same packed-native way — on its
own 4:2:2 samples, not through an RGB conversion — over the one recording that exists for it: no
ffmpeg encoder exists to build a corpus with. Hap is the only one of these whose coded blocks are
themselves an exact format — DXT1/BC1, DXT5/BC3 texture compression — so its own bar is the same
lossless one at a different source: max delta 0 against ffmpeg's decode of the same blocks, over six
streams and six hundred frames, on raw RGB or RGBA planes since Hap carries no chroma subsampling of
any kind for an RGB comparison to be a shortcut around.
Theora and VP3 reach it too, Theora over 1,717 frames across all three of its pixel formats and VP3
over 3,182. 8BPS joins them as well, RGB-native across all three depths it defines — indexed, RGB and
RGB with alpha — over 353 frames of three real files at samples.ffmpeg.org, alpha and colour table
entries included.

MagicYUV is one exception to "against ffmpeg's decode", and in the useful direction: the ffmpeg built
here has its encoder but not its decoder, so the comparison is against the rawvideo that went into the
encoder rather than against another decoder's opinion of what came out. For a lossless codec that is
the stronger oracle of the two, being the ground truth itself. LCL ZLIB is measured the same way in
addition to the usual one, since it too has a real encoder here: round-tripped through it as well as
checked against seven real recordings.

The 33 are Indeo 3, Indeo 4, Indeo 5, TrueMotion 1, WMV1, WMV2, MSS1, MSS2, Canopus HQ/HQA
(`hq_hqa`), Canopus HQX, Lagarith, DV, MSZH, Escape 124, SpeedHQ, VP4, VP7, MSCC, RSCC, WCMV,
MWSC, RASC, Go2Meeting (`g2m`), ScreenPressor (`scpr`), Screenpresso, TSCC2, Sorenson Video 1
(`svq1`), Sorenson Video 3 (`svq3`), Smacker (`smackvid`), Electronic Arts TGQ, TQI and MAD
(`eatgq`, `eatqi`, `eamad`), and Deluxe Paint Animation (`anm`),
and the arguments that settle them are in
`undecodable-codecs.md`. The first four have frames too small to
carry the tables they need —
340 bytes for a 320x240 Indeo 3 picture, 14 for Indeo 4, 2 for Indeo 5, 0 for TrueMotion 1 — so those
tables live in the codec binary and cannot be recovered by reading files. WMV1 and WMV2 both have real ffmpeg
encoders and stop anyway: their run-level, DC and motion-vector tables are the same undocumented ones
MS-MPEG4v3 and WMV1 already need, tied to them by two identical escape constants in the one syntax
document that covers all three, and its own P-frame macroblock type table, `wmv2_inter_table`, is not
shared with either and is published nowhere either — reachable, like the shared tables, only for the
first macroblock of a slice, because a corpus cannot see a second one without already knowing the tables
being sought. MSS1, MSS2, Canopus HQ/HQA and Canopus HQX join the same first group by a different
route: the vendor's own published material — Microsoft's DMO/MFT reference pages for the two screen
codecs, Canopus and Grass Valley's own marketing white papers for the three professional ones — states
no bitstream fact at all, and the only detailed technical write-up found for either family turns out to
be somebody else's account of reverse-engineering the codec, which this project does not build from any
more than it builds from ffmpeg's source directly. Lagarith stops somewhere
more interesting: its frame layer comes out completely and is recorded there, but the range coder
inside it keeps its state in a floating-point variable and its probability header has to reproduce
one implementation's x86 rounding exactly, so the format is defined by an implementation rather than
by anything writable down — and FFmpeg's own decoder for it is recorded as not bit-exact, which
leaves no sound oracle for a codec whose bar is exact equality. DV stops a fourth way: its container
and frame layer are recovered completely and measured against real files, but its two central
tables — the AC-coefficient entropy code and the macroblock shuffle — live in a standard (IEC 61834,
SMPTE 314M) that is not free to read, and the one genuinely independent source that reprints
anything close to them describes a different chroma format from the one this task targets and cannot
be checked against a second rendering. MSZH stops the newest way: its container is ZLIB's, already
decoded, and its "no compression" mode is fully verified across every colour space the format defines,
but the actual compression — "copying blocks from already decoded data," the format's own document says,
then leaves an unfilled placeholder where the algorithm should be — was reverse-engineered against a
single still picture re-encoded six ways rather than a real recording, which yielded exactly two genuine
match tokens to calibrate an entirely unpublished encoding against and settled neither.

Electronic Arts TGQ, TQI and MAD join the same first group, and cleanly: the only detailed description
of any of the three, on MultimediaWiki, is by the same person who wrote every one of ffmpeg's decoders
for them, and that page's own edit history shows its maintainer replacing what it once said about the
shared inverse transform with a link into that decoder's own source file rather than writing the
transform down — the two sibling pages carry the identical gap as an open `<FIXME>`. A DCT decoder
cannot approximate its way past a missing transform the way a container can skip an unread field, so
this is the SVQ1 shape rather than the WMV1 one: not a corpus too small to hold the table, but a source
that names where the table lives instead of printing it.

Deluxe Paint Animation (`anm`) stops the same way from a different direction: its container comes from
Electronic Arts' own first-party documentation, `ANIMFILE.TXT`, released with the format's official
Programmer's Kit and confirmed to be MultimediaWiki's own source at one remove — recovered in full and
verified against three real files down to the record framing and a zero-length record's meaning. What
that document does not cover, and nothing else published does either, is the "RunSkipDump" compression
it names: the only place that scheme is written down at all is inside the same kit's reference source
code, which this project does not transcribe whether the author is a third party or the format's own
vendor.

Escape 124 and SpeedHQ both stop closer to the finish than the rest: their containers, and most of
their bitstreams, are fully mapped and verified against real files, with one specific piece each left
open. Escape 124's container is ARMovie/RPL rather than AVI, and its bitstream's byte order and first
codebook's sizing are confirmed against real frame data. What
remains is one coefficient: the exact bit pattern behind the skip-count coding that the only published
description names "Rice decoding" without ever stating, and every reading tried decodes into
implausibly large skip counts on a key frame that should skip almost nothing. SpeedHQ's field, slice
and DC layers decode exactly against the real ISO/IEC 13818-2 tables this package already carries from
its own MPEG-2 decoder — the codec's own encoder built the corpus, and dozens of whole blocks of real
AC coefficients decode cleanly too — but at least one AC codeword does not match the standard table,
sitting at one bit's difference from four candidates at once with no way to tell which, if any, is
right without forward-transform ground truth this investigation did not build.

VP4 stops closest of all: it shares almost all of its structure with VP3, which this package decodes
exact, and this investigation obtained On2's own `vp4vfw.dll` and confirmed, against a from-scratch bit
parser run on a real inter frame, that every published piece — the header prefix, the flag-array code,
both coded-block-pattern tables, and VP3's own macroblock order and mode scheme reused unchanged —
parses with no desync all the way to the motion-vector section. What is left is exactly one family of
tables: the per-component, magnitude-bucket motion-vector Huffman codes, printed nowhere and stored in
the binary as branches rather than as a table a file could be searched for.

VP7 stops close to VP4, but for the opposite reason: its document is not silent, it is precise about
what it will not say. On2's own "VP7 Data Format and Decoder" predates ffmpeg's decoder by nine years
and gives everything else in full — the boolean coder, the frame header, macroblock features, intra
prediction, and VP7's own 4x4 DCT-II, several tables printed identical to VP8's RFC 6386 ones — but
names `quant_common.c` and `findnearmv.c` by filename at the exact two points, the dequantisation
tables and the interframe motion-vector census, where it declines to print what those files hold. A
flat first macroblock of a real key frame, at two different quantiser indices in two different real
files, needs a DC dequantisation factor no adjustment of VP8's own published tables produces, which is
the gap made concrete: not a missing description, a named and inaccessible one.

SVQ1 stops a fifth way, and it is the cleanest of the group: it does not even need a small frame to make
the case, because its codebook is not carried per-stream at all. The one detailed technical document on
the format — Melanson and Snel's `svq1-format.txt`, which MultimediaWiki's own SVQ1 page states it is
based on — explains the algorithm's shape in full and prints not one codebook or VLC entry, citing
FFmpeg's own `svq1_cb.h`, `svq1_vlc.h` and `svq1.c` in its own words as where those tables actually are.
Its other source, a genuinely independent Utah State University patent on the underlying technology,
states that such tables exist and how large they are without printing one either. The codebook is
"hardwired" into the coding scheme, the document's own word for it, identical in every file — there is
no frame, however large, that was ever going to carry it.

RASC, Go2Meeting (`g2m`), ScreenPressor (`scpr`), Screenpresso, TSCC2, Sorenson Video 1 (`svq1`) and
Sorenson Video 3 (`svq3`), and

All twenty-nine are finished investigations with negative answers, not gaps waiting to be filled.

## What is left, by family

Grouping matters because codecs within a family share a bitstream ancestor, and one decoder usually
opens several names. The families are roughly in descending order of what they buy.

**Modern standards** — `av1`, `vvc`, `dirac`, `snow`, `cavs`, `apv`. HEVC is decoded for intra
pictures; its predicted and bidirectional slices are written but refused, for the reason given in
`README.md`. AV1 still pays twice — a correct one
would close a video codec and a still-image format together — but the AVIF reader's own decoder has
now been examined against the reference and is not a candidate for repair. It reads
equal-probability literal bits where AV1 uses context-indexed CDFs and carries none of the normative
default tables, so it desynchronises at the first partition decision: on a 32x32 still it returns a
flat 130 across the plane where the reference has structure, 1024 samples of 1024 wrong. That path
now refuses. A real AV1 decoder has to be built from the specification, and that is a job on the
scale of this package's H.265 work rather than a gap to be filled in passing.

**Windows Media before VC-1** — `wmv1`, `wmv2`, `msmpeg4v1`, `msmpeg4` (that is version 3), `msp2`,
`mss1`, `mss2`, `msa1`, `mts2`. Version 2 is done. Versions 1 and 3 are argued in `README.md` to be
out of reach on evidence: version 3 chooses per picture between ten tables that are Microsoft's own
and published nowhere, and version 1 has no encoder in existence to derive its tables from or to
check a guess against. WMV1, WMV2, MSS1 and MSS2 are now argued the same way in
`undecodable-codecs.md`, each with its own section. WMV1 and WMV2 stop on the strength of a real
encoder that turns out not to matter: their run-level, DC and motion-vector tables are version 3's
own, tied to it by two identical escape constants in the one document that gives either version's
syntax, and the only macroblocks a corpus can locate without those tables are exactly the ones with no
coded codeword in them to learn the tables from; WMV2 adds a private macroblock-type table of its own
on top of that. MSS1 and MSS2 stop somewhere else: no independent description of their arithmetic
coder exists, the one detailed write-up tracks libavcodec's own function names down to a shared
spelling mistake, and MSS2 additionally embeds Windows Media Video 9 rectangles located by the very
structure that coder would decode. What is left of this family is `msmpeg4v1`, `msmpeg4` (version 3),
`msp2`, `msa1` and `mts2`.

**On2 and RealVideo** — `vp4`, `vp5`, `vp6`, `vp7`, `rv30`, `rv40`, `rv60`. VP3 shares almost all of
its structure with Theora, which is done and exact, so it is the cheapest of these by a wide margin.
VP4, VP6 and VP7 are the three already investigated, and none is implemented, for three different
reasons. VP4 shares almost all of its structure with VP3 too, and its two independent published
sources, plus this project's own verified parse against the real bitstream and against On2's own
`vp4vfw.dll` run directly, confirm all of it except one family of tables: the motion-vector Huffman
codes, printed nowhere and stored in the codec's own binary as branches rather than as a table a file
could be searched for. VP6's specification is public and every table in it was transcribed and checked
back against the document, but the coefficient decode still diverges eight binary decisions into the
first block of the first key frame. VP7's own document is thorough enough to name the two reference
decoder files it declines to print — the dequantisation tables (`quant_common.c`) and the interframe
motion-vector census (`findnearmv.c`) — and a real key frame's flat first macroblock shows exactly why
that gap matters: this project's own reimplementation of everything else the document does print,
checked bit for bit against real files with an independent second decoder, reproduces the wrong DC
value at two different quantiser indices, by an amount no adjustment of VP8's own published tables
accounts for. How far each got, and everything ruled out, are in `undecodable-codecs.md`, so none of
the three searches need be repeated from the start. VP5 is behind VP6's same wall with no published
specification at all.

**Sorenson** — `svq3` is what remains. `svq1` is now argued in `undecodable-codecs.md`: its codebook is
not carried in the stream at all, and the one technical document on the format cites FFmpeg's own source
for the tables rather than printing them.

**Sorenson** — `svq1` is what remains. `svq3` is now argued in `undecodable-codecs.md`: it shares this
package's own H.264 decoder everywhere the two coincide, but its departures from H.264 — its entropy
code chief among them — have no description independent of the one implementation that reverse-engineered
it, confirmed by the format's own recorded history as well as by the page's own citation.

**Lossless RGB and YUV** — what is left of the group is `012v`, `aasc`, `cllc`, `cyuv`, `dxtory`,
`loco`, `m101`, `sheervideo`, `vble` and `ylc`. Ut Video, MagicYUV, ZeroCodec and LCL ZLIB came out
of it and reached exact equality, and MSZH came out of it into `undecodable-codecs.md`. That is the
standard for every one of them: max delta 0 or it is wrong. `v210`, `r210`, `r10k` and `y41p` carry no
compression at all, only a fixed packing of samples into words or byte groups, so there was nothing for
a decoder to get wrong except the layout, and the two RGB ones decode straight into a ten-bit RGB pixel
format with no reduction to eight bits standing in the way of the comparison at all. None of the four
layouts is written down anywhere this project found; all were recovered by sweeping every reading
against ffmpeg's own encoder fed known or pseudo-random samples, and r210 and r10k turned out to
disagree with each other about which ten bits are which despite the family resemblance their names
suggest, while y41p's rows turned out to be coded bottom row first — found only once random content,
which has no row in common with the wrong one, turned a sweep that looked like it matched nothing at
all into an exact match once the row order was reversed. `cljr` is done too, and it is the one lossy
format in this group: the quantisation is the encoder's, so a decoder reading the coded bits has
nothing left to round, but the encoder dithers, which means a coded word is not a plain quantisation of
the source and the sweep that recovered its bit layout had to be checked against another decoder's
reading of the same bits rather than against the picture that went in. This is the densest source of
verifiable wins in the list — but not a uniformly cheap one. `lagarith`, which is arithmetic coding over
the same kind of prediction, also came out of it and is now in `undecodable-codecs.md` instead.

**Screen capture** — `tdsc` and `vmnc` are what remains of this group unattempted. Mostly DEFLATE over
a framebuffer with a delta scheme on top, so also lossless and also absolutely measurable. `flashsv` and
`flashsv2` came out of this group and reached exact equality; see `README.md`. `g2m`, `mscc`, `mwsc`,
`rasc`, `rscc`, `screenpresso`, `scpr`, `tscc2` and `wcmv` came out of it the other way, into
`undecodable-codecs.md`: none of the nine carries an independent bitstream description this project
could confirm, four of them (`mscc`, `wcmv`, `rasc` and `screenpresso`) carry no sample corpus at all,
`mwsc` and `scpr` carry exactly one file each, and `rscc` alone reached a real recovered packet framing
and a delta record's destination coordinates before the two remaining fields resisted every reading
tried.

**Professional and intermediate** — `pixlet`, `prores_raw`,
`aic`, `media100`. `hap` came out of this group and reached exact equality — DXT/BC
texture blocks in a small chunked header, published in full by its own authors, which is what made it
the cheapest of the group rather than merely the best documented. `cfhd` came out of it too, and is the
one member of this group whose free standard, SMPTE ST 2073-1, turned out to state everything a decoder
needs — the tag-value framing, the wavelet transform, the codebook, all of it. It is lossy rather than
exact; see `README.md`. `hq_hqa` and `hqx` looked well
documented too,
on the strength of a MultimediaWiki page each, and turned out not to be: neither Canopus's nor Grass
Valley's own white papers state a bitstream fact, and the one detailed technical description of either
is a reverse engineer's own account of decompiling the codec rather than anything published; they are
counted with the not-implementable codecs above, on the same footing as MSS1 and MSS2. `speedhq` got
further than either — a real encoder to build a corpus with, and a MultimediaWiki page whose vendor is
credited with helping write it — and its field, slice and DC layers, and most of its AC coefficients,
check out exactly against this package's own ISO/IEC 13818-2 tables; what stops it is a small, uncounted
number of AC codewords the page's prose says are "moved around" without saying to where, printed
nowhere except inside that same page's verbatim copy of `libavcodec/speedhq.c`'s own arrays, which this
project does not use. It is counted with the not-implementable codecs above too, closer to Escape 124's
shape than to Canopus's.

The remaining nine are the screen-capture family below this section: MSCC, WCMV, RASC and Screenpresso
carry neither a sample anywhere searched nor an independent bitstream description; MWSC and ScreenPressor
each clear only a one-file corpus, too thin a base to build a table from on this project's own standard,
and ScreenPressor's only detailed technical trace besides is a second author's open-source rebuild of the
vendor's code rather than anything the vendor published; Go2Meeting and TSCC2 each have a real sample
corpus and a genuinely detailed MultimediaWiki page, but each page is either unconfirmed or confirmed to
be its own decoder's author's reverse-engineering notes restated, the same shape MSS1's, MSS2's and
Canopus's pages already turned out to be; and RSCC alone reaches Escape 124's and SpeedHQ's shape — a real
five-file corpus, a packet framing and a delta record's destination coordinates fully recovered and
verified against it, with one header field and two of a record's four fields not resolved. `dvvideo` looked
like the cheapest of these on the same promise — a published standard behind it — but the standard, IEC
61834 and SMPTE 314M, is not free, and the investigation recorded in `undecodable-codecs.md` found no
independent source for its entropy table or a confirmed shuffle table either; it too is counted with
the not-implementable codecs above rather than left in this list.

**Game and FMV codecs** — the largest group, around 45 names, of which `roqvideo`, `interplayvideo`,
`idcinvideo`, `vqavideo`, `eacmv`, `cdxl`, `iff` (IFF ANIM's Byte Vertical Delta, method 5) and `bfi` are
now done and `escape124`, `smackvid`, `eatgq`,
`eatqi` and `eamad` are investigated and not implementable (`undecodable-codecs.md`):
`binkvideo`, `vmdvideo`, `escape130`, the `xan_*` pair and many more. `eatgv` is investigated and
partially recovered rather than either done or closed — see `undecodable-codecs.md`, which records
a container and picture header confirmed to the byte, a published one-byte literal-run formula
measured and corrected, and where the next statement's own bit layout stops matching the file, the
same shape TrueMotion 2's own section there is in. Almost none of what is left has a published
specification; most are described on MultimediaWiki from reverse engineering. Their value is
preservation rather than reach, and each is
small.

**Everything else** — `indeo2`, `asv1`, `asv2`, `mdec`, `mimic`, `amv`, `mxpeg`, `sp5x`,
`truemotion2` and the remainder. `cljr` came out of this group and reached exact equality; it is the
one lossy packing among the fixed layouts, measured against ffmpeg's own decode rather than the
source because its encoder dithers. TrueMotion 2 is the partial case: it is self-describing and
about two thirds recovered, with the evidence in `undecodable-codecs.md`, and it was deliberately not
shipped half-working because a wrong block type in a still passage is indistinguishable from the
codec working.

## What a codec has to clear to be counted

Nothing goes in the first table because it produces a plausible picture. Each one is compared with
ffmpeg's decode of the same bitstream, and the rules that comparison follows were all learned by
getting them wrong first:

- **Compare planes, not RGB**, for any subsampled codec. This library interpolates chroma when
  upsampling and ffmpeg replicates, so an RGB comparison of a 4:2:0 codec shows tens of thousands of
  differing samples at a maximum delta around 130 *even when the decode is exact*. That metric once
  condemned a correct H.263 decoder here, and the same measurement applied to an already-accepted
  MPEG-1 decoder produced 5,928 differing samples of 9,216 — which is how it was caught.
- **Sample every frame, not the endpoints**, and use a long group of pictures (`-g 1000`) so a single
  intra picture anchors the chain. A 25-frame test once read a maximum delta of 3 where the same
  decoder over 100 frames read 204.
- **Read the shape of the error, not only its size.** Flat error across a group of pictures is
  rounding. Error that grows and resets at each intra picture is motion compensation, dequantisation
  or reference handling — a real defect wearing a small number.
- **Know what the oracle actually does.** ffmpeg's default integer inverse transform is itself an
  approximation, so a codec measuring 3 against it measured 1 against `-idct faani`; ffmpeg's
  frame-threaded Theora decode is not deterministic on large frames; `ffprobe` without
  `-fflags +noparse` invents timestamps a container never carried.
- **Know what the tool did to the pictures before you compare them.** Every one of these has produced
  a false alarm here, and each looks exactly like a decoder defect:
  - `ffmpeg -i in out%04d.ppm` runs the image2 muxer at a constant frame rate and **duplicates
    frames** to fill it. It reported a 348-frame file as 824 and a 272-frame one as 3485. Pass
    `-fps_mode passthrough`, and check against `ffprobe -count_frames`.
  - Decoder options go **before** `-i`. `-idct faani` placed after is an output option and silently
    does nothing — with it misplaced a codec measured 6 where it actually measures 2.
  - **PPM carries no alpha.** Comparing an alpha-bearing format through it makes both sides
    composite and invents a large error; one codec read max delta 179 that way and 0 on its planes.
  - Frame files named with a fixed two-digit index sort lexicographically as 10, 100, 11 once a
    stream passes a hundred frames. That put frame 100 against frame 12 and looked precisely like a
    decoder diverging mid-stream.
  - Decode with `-threads 1`: ffmpeg's frame-threaded decode is not deterministic for every codec.

- **Refuse by name.** No `catch` may hand back a blank frame or repeat the last one. That silent
  zero-fill is the worst defect shape in this repository — a wrong picture nothing announces — and
  several instances have been removed from it.

## On licence

The codecs are implemented from published specifications where they exist and from the bitstream
where they do not, with ffmpeg used only as a black-box oracle on its output. Its source is not
transcribed or translated. This package is LGPL-3.0-or-later and carries no FFmpeg copyright notices,
which a derived translation would require.
