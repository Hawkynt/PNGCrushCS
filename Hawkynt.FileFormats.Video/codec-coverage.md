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
| Decoded and verified against ffmpeg | 42 | 20% |
| Established as not implementable from files alone | 8 | 4% |
| Not yet attempted | 161 | 76% |

The 42 are the codec table in `README.md`, counted as distinct libavcodec decoders rather than as
table rows — one row covers several names where a decoder does. Every one was cross-checked frame by
frame against ffmpeg's decode of the same bitstream before it was merged, and the measurements are in
each one's section of that file. The ones that reach exact equality on every sample of every frame
are Microsoft RLE, Microsoft Video 1, Cinepak, QuickTime Animation, Apple Video, Apple Graphics, FLIC, HuffYUV,
FFVHUFF, FFV1, ZMBV, TSCC, CSCD, Flash Screen Video, Flash Screen Video 2, id RoQ, Ut Video, MagicYUV, v210, r210, r10k,
y41p
and CLJR — the two colour-space ones over 883 and 1,446 frames, in all six and all seven of their
colour spaces, and the packed layouts on the sample data itself, at its own coded depth and with
no display conversion in the way, over 120, 90, 90, 90 and 60 frames at three geometries each.
CLJR is the one lossy format among them — measured against ffmpeg's own decode rather than the
source, because its encoder dithers.
Theora and VP3 reach it too, Theora over 1,717 frames across all three of its pixel formats and VP3
over 3,182.

MagicYUV is the one exception to "against ffmpeg's decode", and in the useful direction: the ffmpeg
built here has its encoder but not its decoder, so the comparison is against the rawvideo that went
into the encoder rather than against another decoder's opinion of what came out. For a lossless codec
that is the stronger oracle of the two, being the ground truth itself.

The 8 are Indeo 3, Indeo 4, Indeo 5, TrueMotion 1, WMV1, WMV2, Lagarith and DV, and the arguments that settle
them are in `undecodable-codecs.md`. The first four have frames too small to carry the tables they need —
340 bytes for a 320x240 Indeo 3 picture, 14 for Indeo 4, 2 for Indeo 5, 0 for TrueMotion 1 — so those
tables live in the codec binary and cannot be recovered by reading files. WMV1 and WMV2 both have real ffmpeg
encoders and stop anyway: their run-level, DC and motion-vector tables are the same undocumented ones
MS-MPEG4v3 and WMV1 already need, tied to them by two identical escape constants in the one syntax
document that covers all three, and its own P-frame macroblock type table, `wmv2_inter_table`, is not
shared with either and is published nowhere either — reachable, like the shared tables, only for the
first macroblock of a slice, because a corpus cannot see a second one without already knowing the tables
being sought. Lagarith stops somewhere
more interesting: its frame layer comes out completely and is recorded there, but the range coder
inside it keeps its state in a floating-point variable and its probability header has to reproduce
one implementation's x86 rounding exactly, so the format is defined by an implementation rather than
by anything writable down — and FFmpeg's own decoder for it is recorded as not bit-exact, which
leaves no sound oracle for a codec whose bar is exact equality. DV stops a fourth way: its container
and frame layer are recovered completely and measured against real files, but its two central
tables — the AC-coefficient entropy code and the macroblock shuffle — live in a standard (IEC 61834,
SMPTE 314M) that is not free to read, and the one genuinely independent source that reprints
anything close to them describes a different chroma format from the one this task targets and cannot
be checked against a second rendering. All are finished investigations with negative answers, not
gaps waiting to be filled.

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
check a guess against. WMV1 is now argued the same way in `undecodable-codecs.md`, on the strength of a
real encoder that turns out not to matter: its run-level, DC and motion-vector tables are version 3's
own, tied to it by two identical escape constants in the one document that gives either version's
syntax, and the only macroblocks a corpus can locate without those tables are exactly the ones with no
coded codeword in them to learn the tables from.

**On2 and RealVideo** — `vp4`, `vp5`, `vp6`, `vp7`, `rv30`, `rv40`, `rv60`. VP3 shares almost all of
its structure with Theora, which is done and exact, so it is the cheapest of these by a wide margin.
VP6 is the one already investigated, and it is not like the four above it: its specification is public
and every table in it was transcribed and checked back against the document, but the coefficient
decode still diverges eight binary decisions into the first block of the first key frame. How far it
got, the four errors in that specification it did settle, and everything ruled out are in
`undecodable-codecs.md`, so the search need not be repeated from the start. VP5 is behind the same
wall with no published specification at all.

**Sorenson** — `svq1`, `svq3`. No published specification.

**Lossless RGB and YUV** — around 30 names including `012v`, `aasc`, `cllc`, `cyuv`, `dxtory`,
`loco`, `m101`, `magicyuv`, `mszh`, `r10k`, `r210`, `sheervideo`, `v210`, `vble`, `y41p`, `ylc`,
`zerocodec` and `zlib`. Ut Video came out of this group and reached exact equality, which is the
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

**Screen capture** — `g2m`, `mscc`, `mwsc`, `rasc`, `rscc`, `screenpresso`, `tdsc`, `tscc2`, `vmnc`,
`wcmv`, `scpr`. Mostly DEFLATE over a framebuffer with a delta scheme on top, so also lossless and
also absolutely measurable. `flashsv` and `flashsv2` came out of this group and reached exact
equality; see `README.md`.

**Professional and intermediate** — `cfhd`, `hap`, `hq_hqa`, `hqx`, `pixlet`, `prores_raw`,
`speedhq`, `aic`, `media100`. Mostly well documented. `dvvideo` looked like the cheapest of these on
the same promise — a published standard behind it — but the standard, IEC 61834 and SMPTE 314M, is
not free, and the investigation recorded in `undecodable-codecs.md` found no independent source for
its entropy table or a confirmed shuffle table either; it is counted with the not-implementable
codecs above rather than left in this list.

**Game and FMV codecs** — the largest group, around 45 names, of which `roqvideo` is now done:
`binkvideo`, `smackvid`, `interplayvideo`, `vmdvideo`, `escape124`, `escape130`, the several `ea*`
codecs, the `xan_*` pair and many more. Almost none has a published specification; most are described
on MultimediaWiki from reverse engineering. Their value is preservation rather than reach, and each is
small.

**Everything else** — `indeo2`, `asv1`, `asv2`, `cljr`, `mdec`, `mimic`, `amv`, `mxpeg`, `sp5x`,
`truemotion2` and the remainder. TrueMotion 2 is the partial case: it is self-describing and
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
