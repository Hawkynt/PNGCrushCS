# Hawkynt.FileFormats.Video

[![NuGet](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Video.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Video/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Target](https://img.shields.io/badge/target-net8.0-blue)

> Pure-C# video handling, with demuxing, decoding, encoding and muxing kept as separate contracts so
> packet-level remuxing never has to become a decode/re-encode by accident. The package claims the
> WHOLE domain — every video container and codec, not a selection of it. Where one is missing or only
> partly supported that is a tracked gap, and every gap is in [Format / codec support](#-format--codec-support)
> below: what is read, what is written, and what is not decoded at all with the reason for each.

## 📦 Installation

```bash
dotnet add package Hawkynt.FileFormats.Video
```

Decoded frames use `FileFormat.Core.RawImage`, the same representation exposed by [`Hawkynt.FileFormats.Images`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Images/README.md).

## ✨ Features

- Separate container and codec contracts: demux, decode, encode, and mux are different responsibilities.
- Lazy packet/frame access rather than materializing an entire movie in memory.
- Shared `RawImage` output for decoded frames, enabling the existing image conversion/processing pipeline.
- Container readers and writers for modern, legacy, game, and streaming formats.
- Pure-C# decoders for a broad codec set including MPEG families, VPx, Theora, FFV1, ProRes, DNx, CineForm, classic QuickTime/Windows codecs, screen codecs, and game-video codecs.
- Registry-based container/codec dispatch instead of container-specific decoder plumbing in callers, with decoders and encoders in separate tables so a caller asking what can be written is not told what can be read.
- Packet boundaries are reconstructed according to each container's indexing/lacing/PES rules rather than guessed from byte patterns.
- Packet-level remuxing preserves coded bytes and refuses source representations whose required container state cannot be reproduced honestly.

## 🧩 Format / codec support

Complete, and kept complete by a test rather than by care. Every container the package registers has a
row in the first table, every codec has a row in the second, and every codec investigated and left
undecoded has a row in the third. The first two are re-derived from the compile-time registry by
[`VideoReadmeStateTests`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Tests/Hawkynt.FileFormats.Video.Tests/VideoReadmeStateTests.cs),
which fails the build on any disagreement — a container without a row, a row for a codec nothing
registers, an `Encode` tick with no encoder behind it — so a heading that says "support" cannot
quietly come to mean "some of it". How each codec was measured is in
[`codec-notes.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-notes.md); what stopped the undecoded ones, at bitstream level, is in
[`codec-investigations.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-investigations.md).

### Container support

| Container / stream format | `VideoFormat` | Extensions | Demux | Mux | Oracle | Reference |
| --- | --- | --- | :---: | :---: | --- | --- |
| [Advanced Systems Format (ASF)](https://en.wikipedia.org/wiki/Advanced_Systems_Format) | `Asf` | `.asf`, `.wmv`, `.wma`, `.wm`, `.wmx`, `.asx` | ✅ | ✅ | none | [Microsoft ASF overview](https://learn.microsoft.com/windows/win32/wmformat/overview-of-the-asf-format) |
| [AVI](https://en.wikipedia.org/wiki/Audio_Video_Interleave) | `Avi` | `.avi` | ✅ | ✅ | ffmpeg | [Microsoft AVI RIFF reference](https://learn.microsoft.com/windows/win32/directshow/avi-riff-file-reference) |
| [Flash Video](https://en.wikipedia.org/wiki/Flash_Video) | `Flv` | `.flv`, `.f4v` | ✅ | ✅ | ffmpeg | [FLV format description](https://www.loc.gov/preservation/digital/formats/fdd/fdd000131.shtml) |
| [ISO Base Media / MP4 / QuickTime](https://en.wikipedia.org/wiki/ISO_base_media_file_format) | `Mp4` | `.mp4`, `.m4v`, `.mov`, `.qt`, `.3gp`, `.3g2`, `.m4a` | ✅ | ✅ | ffmpeg | [MP4RA](https://mp4ra.org/) / [Apple QuickTime File Format](https://developer.apple.com/documentation/quicktime-file-format) |
| [Matroska](https://en.wikipedia.org/wiki/Matroska) / [WebM](https://en.wikipedia.org/wiki/WebM) | `Matroska` | `.mkv`, `.mka`, `.mks`, `.mk3d`, `.webm` | ✅ | ✅ | ffmpeg | [Matroska elements](https://www.matroska.org/technical/elements.html) / [WebM container](https://www.webmproject.org/docs/container/) |
| [IVF](https://wiki.multimedia.cx/index.php/IVF) | `Ivf` | `.ivf` | ✅ | ✅ | none | [MultimediaWiki IVF](https://wiki.multimedia.cx/index.php/IVF) |
| [H.264 Annex B byte stream](https://en.wikipedia.org/wiki/Advanced_Video_Coding) | `H264Video` | `.264`, `.h264`, `.avc`, `.x264` | ✅ | ✅ | none | [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264) |
| [H.265 / HEVC Annex B byte stream](https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding) | `H265Video` | `.265`, `.h265`, `.hevc`, `.x265` | ✅ | ✅ | none | [ITU-T H.265](https://www.itu.int/rec/T-REC-H.265) |
| [H.263 elementary byte stream](https://en.wikipedia.org/wiki/H.263) | `H263Video` | `.263`, `.h263` | ✅ | ✅ | none | [ITU-T H.263](https://www.itu.int/rec/T-REC-H.263) |
| [AV1 low-overhead OBU byte stream](https://en.wikipedia.org/wiki/AV1) | `Av1Video` | `.obu` | ✅ | ✅ | none | [AV1 Bitstream & Decoding Process](https://aomediacodec.github.io/av1-spec/) |
| [MPEG Program Stream](https://en.wikipedia.org/wiki/MPEG_program_stream) | `MpegProgramStream` | `.mpg`, `.mpeg`, `.vob`, `.m2p`, `.m2ps` | ✅ | ✅ | none | [MPEG-2 Systems](https://mpeg.chiariglione.org/standards/mpeg-2/systems) |
| [MPEG Transport Stream](https://en.wikipedia.org/wiki/MPEG_transport_stream) | `TransportStream` | `.ts`, `.m2ts`, `.mts`, `.m2t`, `.tsv` | ✅ | ✅ | none | [MPEG-2 Systems](https://mpeg.chiariglione.org/standards/mpeg-2/systems) |
| [Motion JPEG stream](https://en.wikipedia.org/wiki/Motion_JPEG) | `Mjpeg` | `.mjpg`, `.mjpeg` | ✅ | ✅ | ffmpeg | [JPEG / ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [MPEG elementary video stream](https://en.wikipedia.org/wiki/Elementary_stream) | `MpegVideo` | `.m1v`, `.m2v`, `.mpv`, `.mpeg1video`, `.mpeg2video`, `.m1v1`, `.m2v1` | ✅ | ✅ | none | [MPEG-1 Video](https://mpeg.chiariglione.org/standards/mpeg-1/video) / [MPEG-2 Video](https://mpeg.chiariglione.org/standards/mpeg-2/video) |
| [YUV4MPEG2](https://wiki.multimedia.cx/index.php/YUV4MPEG2) | `Yuv4Mpeg` | `.y4m` | ✅ | ✅ | ffmpeg | [MultimediaWiki YUV4MPEG2](https://wiki.multimedia.cx/index.php/YUV4MPEG2) |
| [Ogg](https://en.wikipedia.org/wiki/Ogg) | `Ogg` | `.ogg`, `.ogv`, `.oga`, `.ogx`, `.opus`, `.spx` | ✅ | ✅ | none | [RFC 3533](https://www.rfc-editor.org/rfc/rfc3533) |
| [RealMedia](https://en.wikipedia.org/wiki/RealMedia) | `RealMedia` | `.rm`, `.rmvb`, `.ra`, `.rmj`, `.rms` | ✅ | ✅ | none | [MultimediaWiki RealMedia](https://wiki.multimedia.cx/index.php/RealMedia) |
| [Autodesk FLIC](https://en.wikipedia.org/wiki/FLIC_(file_format)) | `Fli` | `.fli`, `.flc`, `.flx` | ✅ | ✅ | none | [MultimediaWiki FLIC](https://wiki.multimedia.cx/index.php/FLIC) |
| [id RoQ](https://en.wikipedia.org/wiki/RoQ) | `Roq` | `.roq` | ✅ | ✅ | ffmpeg | [MultimediaWiki RoQ](https://wiki.multimedia.cx/index.php/RoQ) |
| [Interplay MVE](https://wiki.multimedia.cx/index.php/Interplay_MVE) | `Mve` | `.mve` | ✅ | ✅ | none | [MultimediaWiki MVE](https://wiki.multimedia.cx/index.php/Interplay_MVE) |
| [id Cinematic](https://wiki.multimedia.cx/index.php/Id_Cinematic) | `Idcin` | `.cin` | ✅ | ✅ | none | [MultimediaWiki CIN](https://wiki.multimedia.cx/index.php/Id_Cinematic) |
| [Westwood VQA](https://wiki.multimedia.cx/index.php/VQA) | `Vqa` | `.vqa` | ✅ | ✅ | none | [MultimediaWiki VQA](https://wiki.multimedia.cx/index.php/VQA) |
| [Smacker](https://en.wikipedia.org/wiki/Smacker_video) | `Smacker` | `.smk` | ✅ | ✅ | none | [RAD Game Tools](https://www.radgametools.com/smkmain.htm) |
| [Electronic Arts Multimedia](https://wiki.multimedia.cx/index.php/Electronic_Arts_Formats) | `Ea` | `.wve`, `.cmv`, `.tgv`, `.uv`, `.uv2` | ✅ | ✅ | none | [MultimediaWiki EA formats](https://wiki.multimedia.cx/index.php/Electronic_Arts_Formats) |
| [BFI](https://wiki.multimedia.cx/index.php/Brute_Force_%26_Ignorance) | `Bfi` | `.bfi` | ✅ | ✅ | none | [MultimediaWiki BFI](https://wiki.multimedia.cx/index.php/Brute_Force_%26_Ignorance) |
| [Commodore CDXL](https://en.wikipedia.org/wiki/CDXL) | `Cdxl` | `.cdxl` | ✅ | ✅ | none | [MultimediaWiki CDXL](https://wiki.multimedia.cx/index.php/CDXL) |
| [IFF ANIM](https://en.wikipedia.org/wiki/ANIM) | `Anim` | `.anim`, `.iff` | ✅ | ✅ | none | [Amiga ANIM IFF](https://wiki.amigaos.net/wiki/ANIM_IFF_Animation) |
| [Sierra VMD](https://wiki.multimedia.cx/index.php/Sierra_VMD) | `Vmd` | `.vmd` | ✅ | ✅ | none | [MultimediaWiki VMD](https://wiki.multimedia.cx/index.php/VMD) |
| [PlayStation STR](https://wiki.multimedia.cx/index.php/PlayStation_STR) | `Str` | `.str` | ✅ | ✅ | none | [MultimediaWiki STR](https://wiki.multimedia.cx/index.php/PlayStation_STR) |
| [ARMovie/RPL](https://wiki.multimedia.cx/index.php/ARMovie) | `Rpl` | `.rpl` | ✅ | ✅ | none | [MultimediaWiki ARMovie](https://wiki.multimedia.cx/index.php/ARMovie) |

**Oracle** names the program outside this repository that has read a file this container's muxer
wrote — [ffmpeg](https://ffmpeg.org/) throughout. `none` means nothing but this package's own
demuxer has ever opened one, which for most of the containers here is the answer: a muxer and a
demuxer written from the same reading of a format will agree with each other whether or not that
reading is right, and a file only the writer's own reader can open is not interchange. The claim is
declared by `[VerifiedBy]` on the writer type, carried into `VideoFormatEntry.VerifiedBy` by the
registry generator, and held to what it says by
[`EncoderOracleTests`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Tests/Hawkynt.FileFormats.Video.Tests/EncoderOracleTests.cs),
which writes a clip into the container and requires the named tool to decode a frame of it back at
the size that went in.

### Codec support

Every codec the package registers has a row, and the name in the first column is the codec's own
`CodecName` — the same string a refusal message names it by. `Decode` is what
[`VideoFormatRegistry.CreateDecoder`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/VideoFormatRegistry.cs) builds, 100 of them; `Encode` is
what [`VideoFormatRegistry.CreateEncoder`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/VideoFormatRegistry.cs) builds, 70 of them. The two
are separate tables in the registry because they are looked up by different things: a decoder by a
whole stream description, an encoder by the four-character code a caller wants written.

**⚠️ means the decoder refuses something the format itself defines** — a profile, depth, variant or
mode a conforming encoder may write — and the Notes column says which. **✅ means every layout the
format defines is decoded**; such a codec still refuses malformed input, undefined field values and
forms no encoder produces, because a plausible wrong picture is worse than a refusal. No codec
silently misdecodes what it will not read. An `Encode` tick is a lossless or format-faithful writer.
Where the codec has a lossless form the encoder writes it and refuses by name any picture it would
have to reduce to fit; where the format itself is lossy — Motion JPEG, Microsoft Video 1, Cinepak, DV,
Microsoft's MPEG-4, ASUS V1 and V2, Apple Video, Apple Motion JPEG-B, GoPro CineForm, Intel Indeo 2, RealVideo 1, H.261, H.263, H.265, MPEG-1 video, MPEG-2 video, MPEG-4 Part 2, On2 VP3, Theora, Hap, Apple ProRes, id RoQ, Intel Indeo 4 — the row says so, because there is nothing else such an encoder
could write. Codec-by-codec provenance and
measurement notes are in
[`codec-notes.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-notes.md).

| Codec | Decode | Encode | Oracle | Implemented scope / note | Reference |
| --- | :---: | :---: | --- | --- | --- |
| [Motion JPEG](https://en.wikipedia.org/wiki/Motion_JPEG) | ✅ | ✅ | ffmpeg | `MJPG`, `jpeg`, `V_MJPEG`; each packet one whole JPEG. The encoder writes baseline JPEG through the image package's own writer, every packet a key frame | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [Apple Motion JPEG-B](https://en.wikipedia.org/wiki/Motion_JPEG) | ✅ | ✅ | ffmpeg | `mjpb`; baseline JPEG behind a 48-byte field header, one or two fields a packet. The header layout is recovered by measurement, not published. The encoder writes one field a sample with its tables inline, every offset the QuickTime description requires aligned to sixteen bytes, and entropy data without JPEG's byte stuffing. The JPEG coding itself is the image package's baseline writer, so the loss is baseline JPEG's and nothing is added on top of it | [MultimediaWiki MJPEG](https://wiki.multimedia.cx/index.php/MJPEG) |
| [Avid AVRn](https://wiki.multimedia.cx/index.php/Motion_JPEG) | ✅ | ✅ | ffmpeg | `AVRn`; overloaded by history — without Avid's `1:1` codec marker it is Motion JPEG, with it the uncompressed Resolution 1:1 path, and the container's picture size is trusted over the JPEG frame header's. The encoder writes Resolution 1:1: packed UYVY 4:2:2 with no prediction, transform or entropy coding, so a 4:2:2 picture round-trips sample-exactly | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [MPEG-1 video (ISO/IEC 11172-2)](https://en.wikipedia.org/wiki/MPEG-1) | ⚠️ | ✅ | ffmpeg | I, P and B pictures. D pictures (2.4.2.8) refused by name. The encoder writes I and P pictures in groups of twelve, choosing per macroblock between a forward prediction on a searched whole-pixel vector and not coding the macroblock at all; vectors are coded with forward_f_code 2, so motion reaches thirty-two pixels rather than the sixteen an f_code of one would cap it at. Each prediction is made against the picture a decoder driven by this encoder's own output holds, not against the source, so nothing drifts along a group. It writes no B pictures: those reorder coding against display, which would change what this encoder hands its caller rather than only what it writes, and they express nothing a P picture cannot here. Lossy by construction, because MPEG-1 is — every block goes through the transform and a quantiser and the standard has no lossless form | [ISO/IEC 11172-2](https://www.iso.org/standard/22411.html) |
| [MPEG-2 video (ISO/IEC 13818-2)](https://en.wikipedia.org/wiki/H.262/MPEG-2_Part_2) | ⚠️ | ✅ | ffmpeg | Frame pictures at 4:2:0 and 4:2:2, including field DCT and field motion compensation. Field pictures, dual-prime prediction, 4:4:4 High profile and the scalability extensions refused. The encoder writes progressive 8-bit 4:2:0 Main Profile @ Main Level I and P pictures in groups of twelve, one slice per macroblock row, choosing per macroblock between a forward prediction on a searched vector and not coding the macroblock at all. Vectors are whole-pixel in luminance and so land between chrominance samples half the time, where the prediction is formed by the decoder's own interpolation rather than rounded to the nearer sample. It refuses geometry and rates outside the level it declares rather than mislabelling them | [ITU-T H.262](https://www.itu.int/rec/T-REC-H.262) |
| [MPEG-4 Part 2 video (ISO/IEC 14496-2)](https://en.wikipedia.org/wiki/MPEG-4_Part_2) | ⚠️ | ✅ | ffmpeg | Rectangular, progressive, 8-bit 4:2:0 I/P/B. Quarter-sample vectors, sprites/GMC, interlace, OBMC, data partitioning, scalability, shape coding, newpred, reduced resolution and the complexity-estimation header each refused where signalled. The encoder writes Advanced Simple I/P/B streams at quantiser 8, an I-VOP every twelve displayed pictures with two B-VOPs between anchors. P-VOPs carry a searched vector or nothing at all; B-VOPs choose forward, backward or interpolated prediction on vectors searched against both anchors, and a macroblock the following anchor did not code is not carried at all. Every packet repeats its VOL, so VFW-style containers need no out-of-band configuration. MPEG-4 Part 2 has no lossless mode in this coding path | [ISO/IEC 14496-2](https://www.iso.org/standard/39259.html) |
| [H.261 (ITU-T H.261)](https://en.wikipedia.org/wiki/H.261) | ⚠️ | ✅ | ffmpeg | QCIF and CIF, clauses 3 and 4 entire, in-loop filter included. Annex D still-image transmission refused. The encoder writes those same two formats and refuses every other picture size by name — clause 3.1 defines two and one bit of PTYPE names which, so there is no syntax here for a third — coding an intra picture every twelfth frame and choosing per macroblock between an intra coding, a prediction with or without a searched whole-pixel vector, either of those with the loop filter of clause 3.2.3, and not transmitting the macroblock at all. It never states MQUANT: one quantiser per group of blocks, held across the picture. Lossy by construction, because H.261 is — every coded block goes through the transform and a five-bit quantiser and the Recommendation has no lossless form at all | [ITU-T H.261](https://www.itu.int/rec/T-REC-H.261) |
| [H.263 (ITU-T H.263 baseline, and Sorenson Spark)](https://en.wikipedia.org/wiki/H.263) | ⚠️ | ✅ | ffmpeg | Baseline (clauses 5-6) and Sorenson Spark decoded. Annexes C, D, E, F, G and T, and the extended PTYPE of 5.1.4, each refused where signalled. The encoder writes the five standard Table 5 formats — sub-QCIF, QCIF, CIF, 4CIF and 16CIF — in groups of twelve at picture quantiser 8, with no annex modes and no optional GOB headers, choosing per macroblock between a forward prediction on a searched vector and not transmitting the macroblock at all. Vectors are coded against 6.1.1's median of three neighbours, not against the previous macroblock's. Other picture sizes need the extended PTYPE and are refused by name. Lossy by construction, because H.263 is | [ITU-T H.263](https://www.itu.int/rec/T-REC-H.263) |
| [H.264/AVC (ITU-T H.264 \| ISO/IEC 14496-10)](https://en.wikipedia.org/wiki/Advanced_Video_Coding) | ⚠️ | ✅ | ffmpeg | Progressive 8-bit 4:2:0; CAVLC and CABAC I/P/B slices, High-profile 8x8 transform and scaling lists, long-term references, weighted and direct prediction. 4:2:2/4:4:4, depths above 8 bits, interlace/MBAFF, FMO, data partitioning, SVC/MVC and lossless bypass refused. The encoder writes Baseline-profile IDR pictures made of `I_PCM` macroblocks, so every coded sample is carried verbatim and a 4:2:0 source round-trips sample for sample; the only loss a non-YUV source incurs is the conversion to 4:2:0. It writes no predicted slices: those need the CAVLC and CABAC residual syntax in the write direction, and the entropy coders here decode only | [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264) |
| [H.265/HEVC (ITU-T H.265 \| ISO/IEC 23008-2)](https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding) | ⚠️ | ✅ | ffmpeg | Main profile, 8-bit 4:2:0; intra and inter slices, reference management, CABAC, weighted prediction, scaling lists, deblocking, SAO, tiles and dependent slices. The writer emits every even-sized frame as an independent Main-profile IDR picture made from 32x32 PCM coding units, with VPS/SPS/PPS in `HEVCDecoderConfigurationRecord` private data and length-prefixed samples. PCM samples are stored exactly, so the only loss is the 4:2:0 conversion RGB input goes through; a 4:2:0 source comes back unchanged. It writes no inter pictures: those need a CABAC arithmetic *encoder* and the transform, quantisation and residual syntax behind it, none of which exists here — the CABAC engine in this package decodes only. The decoder handles that uniform PCM shape as well as its general slice path; arbitrary PCM coding units elsewhere, the format range extensions, screen-content coding, multilayer/3D and separate colour planes are refused | [ITU-T H.265](https://www.itu.int/rec/T-REC-H.265) |
| [VC-1 / Windows Media Video 9 (SMPTE 421M, Simple and Main profile intra pictures)](https://en.wikipedia.org/wiki/VC-1) | ⚠️ | ✅ | ffmpeg | `WMV3`, Simple and Main profile **intra pictures only**; sequence header read from container private data. Predicted, bidirectional and skipped pictures, Advanced profile (`WVC1`, `WMVA`), MULTIRES, RANGERED and LOOPFILTER each refused by name. The encoder writes progressive Main-profile intra pictures: the analytical forward transform of Annex A.2, the uniform quantiser the decoder reverses, a predicted DC coefficient and every non-zero AC coefficient through Escape Mode 3. It writes no predicted pictures — those need a motion and prediction layer this writer does not have. Lossy by construction, and measurably so at the direct-current step 8.1.1.1 fixes for a given quantiser | [SMPTE ST 421](https://ieeexplore.ieee.org/document/7290900) |
| [Microsoft MPEG-4 versions 1 to 3 video (MPG4/MP42/MP43)](https://wiki.multimedia.cx/index.php/Microsoft_MPEG-4) | ⚠️ | ✅ | ffmpeg | All three variants, intra and predicted pictures: version 1 (`MPG4`, `MP41`, `DIV1`), version 2 (`MP42`, `DIV2`) and version 3 (`MP43`, `DIV3`-`DIV6`, `DVX3`, `AP41`, `AP42`, `COL0`, `COL1`, `MPG3`, and Matroska's `V_MPEG4/MS/V3`). Windows Media Video 7 and 8 (`WMV1`, `WMV2`) are accepted by the registry and then refused by name. The encoder writes any of the three, one slice a picture, one motion vector a macroblock and no alternating current prediction; the registry routes `MP43` to it | [DIVX3/MS-MPEG4 v1-v3](https://wiki.multimedia.cx/index.php/Microsoft_MPEG-4) |
| [RealVideo 1 (RV10/RV13)](https://en.wikipedia.org/wiki/RealVideo) | ⚠️ | ✅ | ffmpeg | `RV10`, `RV13` at bitstream revision 0; H.263 macroblock layer under RealVideo's own slice header. PB-frames refused. RealVideo 2/3/4 are not claimed at all, so they reach the registry's own "no codec decodes this" refusal. The encoder writes RV10 revision-zero intra and predicted pictures in groups of twelve, one run covering the whole picture, over the same H.263 macroblock layer the decoder hands its pictures to — so the median vector predictor, the complemented CBPY of an inter macroblock and the COD rule are stated once and serve both directions. Lossy by construction: every coded block goes through the transform and a quantiser | [ITU-T H.263](https://www.itu.int/rec/T-REC-H.263) |
| [On2 VP3](https://en.wikipedia.org/wiki/VP3) | ✅ | ✅ | ffmpeg | VP3.1 (`VP31`, `VP32`) entire, and `VP30`, whose key frames carry sixteen fewer header bits and are normalised into the VP3.1 shape rather than read by a second decoder. The encoder writes VP3.1 intra and inter frames in groups of twelve, choosing per macro block between a searched whole-sample vector, no displacement, and -- a whole super block at a time -- not coding the blocks at all. Modes and vectors are written in the literal forms the format offers, and the golden frame is never referenced. Lossy by construction: every coded block goes through the transform and a quantiser, and VP3 has no lossless form | [Theora specification, Appendix B](https://www.theora.org/doc/Theora.pdf) |
| [VP8 (RFC 6386)](https://en.wikipedia.org/wiki/VP8) | ✅ | ✅ | ffmpeg | RFC 6386 entire. Reserved bitstream versions and the reserved colour-space/clamping fields refused. The encoder writes key frames only, which RFC 6386 permits a whole stream to be, through the image package's VP8 writer — inter frames would need a second prediction and entropy encoder beside that one, and the row says so rather than the tick implying otherwise. Lossy by construction: every block goes through the transform and a quantiser | [RFC 6386](https://www.rfc-editor.org/rfc/rfc6386) |
| [VP9 (profiles 0-3)](https://en.wikipedia.org/wiki/VP9) | ✅ | ✅ | ffmpeg | Profiles 0-3: 8-, 10- and 12-bit, 4:2:0 and the non-4:2:0 layouts, and the full-range sRGB/GBR representation | [WebM VP9](https://www.webmproject.org/vp9/) |
| [Theora (Xiph.Org Theora I)](https://en.wikipedia.org/wiki/Theora) | ✅ | ✅ | ffmpeg | Xiph.Org Theora I, all three pixel formats. Bitstream versions other than 3.2, the reserved pixel format and set reserved bits refused. The encoder writes 4:4:4 all-intra pictures with canonical Xiph headers, one constant quantisation matrix and raster-order DC prediction, and represents every 8x8 block by its DC coefficient alone: ordinary Theora I syntax, deliberately coarse, and lossy by construction. Its header declares the one base matrix twice, because the field a quant range names a matrix with is no field at all at one matrix by the specification and one bit by FFmpeg's reader, and only from two upwards do the two agree | [Theora specification](https://www.theora.org/doc/Theora.pdf) |
| [FFV1 (RFC 9043)](https://en.wikipedia.org/wiki/FFV1) | ⚠️ | ✅ | ffmpeg | Versions 0, 1 and 3, both entropy coders, slices and checksums; 8-bit samples only. Version 2 and deeper samplings refused. The encoder writes version 3 with the range coder, the small context model, a slice checksum on every slice and every frame a key frame — what `ffmpeg -c:v ffv1 -level 3 -coder 1 -context 0 -slicecrc 1 -g 1` writes | [RFC 9043](https://www.rfc-editor.org/rfc/rfc9043) |
| [Apple ProRes](https://en.wikipedia.org/wiki/Apple_ProRes) | ⚠️ | ✅ | ffmpeg | `apco`, `apcs`, `apcn`, `apch` at 4:2:2 and `ap4h`, `ap4x` at 4:4:4; bitstream versions 0 and 1. Reserved chroma formats, reserved interlace mode and reserved alpha types refused. Samples are clamped to RDD 36's permissible video levels, which scale with the depth, so a 4:4:4 picture differs from ffmpeg by up to twelve levels at the two extremes of the range — ffmpeg holds its lower clamp at 4 at both depths — and by no more than one anywhere else. The encoder writes the four 4:2:2 profiles, progressive, ten-bit and without alpha at bitstream version 0, each with its own quantisation weight matrices and one quantisation index a picture, bisected to meet the profile's published data rate; `ap4h` and `ap4x` are refused by name, being 4:4:4 at twelve bits with an alpha channel. ProRes quantises transform coefficients, so it is lossy by construction and only a picture already on the quantiser's own grid comes back exactly. ffmpeg decoded all 64 streams written here without a warning and agrees with this package's own decode of them to within one level on every one of 150,087,847 samples of `yuv422p10le` | [Apple ProRes white paper](https://www.apple.com/final-cut-pro/docs/Apple_ProRes_White_Paper.pdf) |
| [Avid DNxHD / DNxHR (SMPTE VC-3)](https://en.wikipedia.org/wiki/DNxHD_codec) | ⚠️ | — | — | SMPTE VC-3 header versions 1-3, progressive 4:2:2 and 4:4:4. Interlaced frames, 4:2:0, alpha-bearing compression IDs and RGB-mode macroblocks refused | [SMPTE VC-3 overview](https://ieeexplore.ieee.org/document/7290708) |
| [DV (IEC 61834 / SMPTE 314M)](https://en.wikipedia.org/wiki/DV_%28video_format%29) | ⚠️ | ✅ | ffmpeg | `dvsd`, `dv25`, `dv50`, `dvsl`, `cdvc`, `CDV5`, `dvis`, `pdvc`, `SL25`, `SLDV` and QuickTime's nine spellings (`dvc `, `dvcp`, `dvcs`, `dvl `, `dvlp`, `dvpp`, `dv5n`, `dv5p`, `AVdv`); the six standard-definition profiles — 525/60 and 625/50 at 25 Mbit in 4:1:1 and 4:2:0, and DVCPRO50 at 4:2:2 — with both transform modes. The profile comes from the frame, never from the tag. DVCPRO HD (SMPTE 370M, `dvhd`, `dvh1`-`dvh6`, `dvhq`, `dvhp`, `CDVH`) is accepted and then refused by name. The encoder writes five of the six profiles, the picture's own colour sampling choosing which — 4:2:2 planar asks for DVCPRO50 — and is lossy because DV is: a frame is a fixed length whatever is in it, so a picture that will not fit is quantised until it does. Handed the same planes it writes byte for byte what ffmpeg's own DV encoder writes | [SMPTE 314M](https://ieeexplore.ieee.org/document/7291838) |
| [GoPro CineForm](https://en.wikipedia.org/wiki/CineForm) | ⚠️ | ✅ | ffmpeg | SMPTE VC-5 three-channel layouts: 10-bit 4:2:2 YUV and 12-bit RGB. Alpha-bearing channel counts and lowpass precisions other than 16 bits refused. The encoder writes progressive ten-bit 4:2:2 I-frames: three spatial 2/6 wavelet levels, one raw sixteen-bit lowpass and Annex-C codebook highpasses, sharing its transform, companding and codebook with the decoder so the two directions cannot drift apart. CineForm is intra-only in this path, so every picture is one whole packet. Lossy by construction: the wavelet coefficients are quantised and the format has no lossless form in this layout | [LOC CineForm overview](https://www.loc.gov/preservation/digital/formats/fdd/fdd000458.shtml) |
| Hap | ⚠️ | ✅ | ffmpeg | `Hap1`, `Hap5`, `HapY`, `HapM`, `HapA`, `Hap7`, `HapH`. Texture formats and second-stage compressors the codec does not know, and image combinations Hap does not define, refused. Neither Wikipedia nor MultimediaWiki carries a page for this format; the format's own documentation is the overview. The encoder writes `Hap1`, `Hap5` and `HapY` — the three pixel formats ffmpeg's own Hap encoder writes, and so the three there is a second encoder to measure against — as one Snappy-compressed section a frame, and refuses `HapM`, `HapA`, `Hap7` and `HapH` by name rather than assembling a texture nothing could check. The coding is lossy by construction: DXT quantises each 4x4 block to two colour endpoints, so only a picture the format can hold exactly comes back exactly — every one of the 65536 colours the block decode's own 5-6-5 widening states, one or two to a block, and every alpha value a block holds alone | [Vidvox Hap `HapVideoDRAFT.md`](https://github.com/Vidvox/hap/blob/master/documentation/HapVideoDRAFT.md) |
| Matrox Uncompressed SD | ✅ | — | — | `M101`; 8- and 10-bit 4:2:2 read from the 24-byte Matrox AVI trailer. Odd widths and other sample depths refused. Adapted from FFmpeg's LGPL-2.1-or-later decoder. No neutral overview of this format is published | [FFmpeg `m101.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/m101.c) |
| Avid 1:1 10-bit RGB Packer (avrp) | ✅ | ✅ | ffmpeg | `AVrp`; little-endian 10-bit RGB word, rows padded to 64-pixel blocks. Word and padding recovered by measurement — nothing about this layout is published. The encoder writes the same words and the same block padding, byte for byte what ffmpeg's own `avrp` encoder writes | — |
| [Avid Meridien Uncompressed](https://wiki.multimedia.cx/index.php/AVUI) | ⚠️ | ✅ | ffmpeg | `AVUI`; UYVY 4:2:2 in one field or two behind a fixed run of blank lines, and the inverted-alpha companion a depth-32 sample carries behind that. The APRG atom of the sample description says which field mode a stream is in; with no APRG the reference decoder's interlaced default applies, and 486-line NTSC stores its odd field first where 576-line PAL stores its even field first. Only 720x486 and 720x576 accepted, the two geometries the format's own encoder writes, and only 16- and 32-bit samples. The encoder writes that same layout, progressive unless the stream description carries an APRG marking interlace, and refuses a transparent picture at depth 16 where the format has nowhere to put alpha. Every picture is a key frame and the packing is lossless, so a picture already on its 4:2:2 grid comes back exactly. Measured against ffmpeg 9.0.1: this encoder writes byte for byte what ffmpeg's own avui encoder writes at both geometries in both field modes, and ffmpeg's decoder reads all 1,529,280 alpha samples of the four depth-32 cases back exactly as they went in | [MultimediaWiki AVUI](https://wiki.multimedia.cx/index.php/AVUI) |
| [Microsoft Video 1](https://wiki.multimedia.cx/index.php/Microsoft_Video_1) | ✅ | ✅ | ffmpeg | `CRAM`, `MSVC`, `WHAM` at 8-bit palettised and 16-bit 5-5-5. Other depths, and pictures that are not a whole number of 4x4 blocks, refused. The encoder writes `MSVC` at both depths, choosing per block between one colour, two, eight and a skip run; the coding is lossy by construction — two colours to a block — so only a picture the format can hold exactly comes back exactly | [MultimediaWiki](https://wiki.multimedia.cx/index.php/Microsoft_Video_1) |
| [Microsoft RLE](https://wiki.multimedia.cx/index.php/Microsoft_RLE) | ✅ | ✅ | ffmpeg | `MRLE`, `BI_RLE8`, `BI_RLE4`; 4- and 8-bit bottom-up frames with delta and skip escapes. Top-down heights and other depths refused. The encoder writes 4- and 8-bit palettised frames from indexed pictures only, using the delta escapes for what did not change; a picture that is not palettised is refused rather than quantised | [MultimediaWiki Microsoft RLE](https://wiki.multimedia.cx/index.php/Microsoft_RLE) |
| [Cinepak](https://en.wikipedia.org/wiki/Cinepak) | ✅ | ✅ | ffmpeg | `cvid`, `CVID`; QuickTime and AVI alike. Strips that are not a whole number of 4x4 blocks, unknown chunk types and mid-stream size changes refused. The encoder writes `cvid`, pricing each strip's blocks between one codebook entry, four and a skip; the coding is lossy by construction — four luminances and one chrominance pair to sixteen pixels — and its colour space can state only 2669700 of the 16777216 colours exactly, all 256 greys and all eight corners of the colour cube among them | [MultimediaWiki Cinepak](https://wiki.multimedia.cx/index.php/Cinepak) |
| [Intel Indeo 2](https://wiki.multimedia.cx/index.php/Indeo_2) | ✅ | ✅ | ffmpeg | `RT21`; Huffman-coded sample pairs against one of four delta tables, intra frames predicting from the line above and inter frames from the frame before. The picture's width must divide by eight and its height by four, which is what coding pairs into quarter-size chrominance planes means. The encoder writes both, in groups of twelve, trying every delta table and keeping the least-error result; an inter frame predicts from the encoder's own reconstruction, and a pair the previous frame already states well enough is skipped. Lossy by construction: every pair is quantised to one of the table's entries and the format has no lossless form | [MultimediaWiki Indeo 2](https://wiki.multimedia.cx/index.php/Indeo_2) |
| [Intel Indeo 3](https://wiki.multimedia.cx/index.php/Indeo_3) | ⚠️ | ✅ | ffmpeg | `IV31`, `IV32`; a binary tree cutting each plane into cells, motion compensation, and vector quantisation over 4x4 to 8x8 blocks. Eight-bit samples and half-sample motion vectors are flagged in a frame header and refused — no encoder is known to have written either — as is the "skip cell" null code, whose effect on the two frame buffers is stated nowhere | [MultimediaWiki Indeo 3](https://wiki.multimedia.cx/index.php/Indeo_3) |
| [Intel Indeo Video Interactive 4](https://en.wikipedia.org/wiki/Indeo) | ⚠️ | ✅ | ffmpeg | `IV41`; picture and band headers, the wavelet subdivision into one band or four, tiling, the slant and Haar transforms in two dimensions and in each one alone at both block sizes, the band that stores its samples untransformed, the fifteen scan patterns a band header may select by index, its nine eight-by-eight dequantisation matrices and its five four-by-four ones, run-value coded coefficients with the nine built-in maps and the per-band permutations of them, the sixteen built-in codebooks and the custom ones a stream spells out, whole- and half-sample motion compensation, bidirectional frames including the second frame Indeo 4 packs into the first one's packet, and the Haar recomposition of a scalable picture. Refused by name: the five of the format's eighteen transforms Intel specified and never shipped — the four discrete cosine transforms and the four-by-four pass-through — a band spelling out a scan pattern or a dequantisation matrix of its own, and a chrominance subsampling other than YVU9. The encoder writes self-contained YVU9 intra pictures with one band per plane, 256-sample luma tiles, direct 8x8 luminance and 4x4 Haar chrominance, quantiser zero and custom fixed six-bit codebooks; every packet is a key frame and no motion-prediction or rate-control policy is invented. A block that holds nothing but the running DC prediction goes into the coded-block pattern as uncoded, because a coded block opening on its end-of-block symbol is block data out of step with the bitstream rather than an empty block. RGB round trips are lossy because IV41 subsamples chrominance 4:1:0. Measured against ffmpeg 9.0.1 on five decoder clips from `samples.ffmpeg.org`: 17,261 pictures, 1,910,624,004 samples of `yuv410p`, no difference on any plane of any frame | [FFmpeg `indeo4.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/indeo4.c) |
| [Intel Indeo Video Interactive 5](https://en.wikipedia.org/wiki/Indeo) | ⚠️ | — | — | `IV50`; group, picture and band headers, the wavelet subdivision into one band or four, tiling, the slant transform in two dimensions and in each one alone, the band that stores its samples untransformed, the four-by-four chrominance transform, run-value coded coefficients with the nine built-in maps and the per-band permutations of them, the sixteen built-in codebooks and the custom ones a stream spells out, half-sample motion compensation, quantiser and motion-vector inheritance between bands, and the five-three recomposition of a scalable picture. Refused by name: a password-protected clip, whose frames are scrambled with a key the file does not carry; the YV12 picture format and four-by-four luminance blocks, both defined and neither ever written; and a band header carrying extended transform information. Measured against ffmpeg 9.0.1 on fifteen clips from `samples.ffmpeg.org`: 5,516 pictures, 936,074,736 samples of `yuv410p`, no difference on any plane of any frame | [FFmpeg `indeo5.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/indeo5.c) |
| [QuickTime Animation (RLE)](https://en.wikipedia.org/wiki/QuickTime_Animation) | ✅ | ✅ | ffmpeg | `rle `; depths 1, 2, 4, 8, 16, 24 and 32, plus greyscale 33, 34, 36 and 40. A palettised stream with no colour table is refused rather than drawn through a guessed palette. The encoder writes 8-bit palettised, 24- and 32-bit frames, choosing each line's opcodes by the reference encoder's own dynamic programme. 16-bit is not written | [MultimediaWiki QuickTime RLE](https://wiki.multimedia.cx/index.php/Apple_QuickTime_RLE) |
| [Apple Video (RPZA)](https://en.wikipedia.org/wiki/Apple_Video) | ✅ | ✅ | ffmpeg | `rpza` in QuickTime, `azpr` in AVI; 15-bit RGB vector quantisation over 4x4 blocks. The encoder writes `rpza`, choosing per block between a run of skips, a run of one colour and a block stating all sixteen of its pixels; the four-colour opcode is read and never written — the reference encoder's own path for it never runs at its own thresholds, and forced it exchanges red and blue. Lossy by construction — 15-bit colour, and blocks — but bounded: no channel of any pixel comes back more than one level of 32 from the picture handed in, and nothing accumulates over a stream. Measured against ffmpeg, which has an `rpza` encoder as well as a decoder, in RGB555 rather than RGB so that no colour conversion sits between the two: this decoder agrees with ffmpeg's on all 108,728,920 pixels of the 21 RPZA streams published at samples.ffmpeg.org, this encoder writes byte for byte what ffmpeg's writes on all 3,830 of their frames and on 149 frames of 13 clips ffmpeg generated, and ffmpeg's decode of what it writes is identical to this package's on every pixel | [MultimediaWiki Apple RPZA](https://wiki.multimedia.cx/index.php/Apple_RPZA) |
| [Apple Graphics (SMC)](https://en.wikipedia.org/wiki/QuickTime_Graphics) | ✅ | ✅ | ffmpeg | `smc `; 8-bit palettised only. The colour table comes from the sample description, or from the QuickTime default where the stream names none; a stream naming a system colour resource by number is refused. The encoder writes the same eight-bit indices and is exact rather than lossy, because every block of a palettised picture is representable — one to eight colours by the shared-colour opcodes, more than eight by the sixteen raw indices — so a picture that is not `Indexed8` is refused by name instead of being reduced to 256 colours, as are depth 40, a missing palette, and a palette or picture size that changes mid-stream. It resets the three colour caches with every packet and never names an entry that packet has not written, which is what makes its output read the same whether or not a decoder carries those caches between chunks — the one point on which the two readings of this format differ. 27 files it wrote, 394 frames, come back from ffmpeg's own SMC decoder with the index plane and the colour table identical; of 14 files ffmpeg's encoder wrote — 262 frames — this decoder agrees with ffmpeg's on 3 and differs on 11, in the carried-over caches and nowhere else | [MultimediaWiki Apple SMC](https://wiki.multimedia.cx/index.php/Apple_SMC) |
| [Apple Planar RGB (8BPS)](https://wiki.multimedia.cx/index.php/8BPS) | ⚠️ | ✅ | ffmpeg | `8BPS` at 8-bit palettised, 24-bit RGB and 32-bit RGB with alpha. A named system colour resource is refused rather than substituted | [MultimediaWiki 8BPS](https://wiki.multimedia.cx/index.php/8BPS) |
| [Autodesk Animator Codec](https://wiki.multimedia.cx/index.php/Autodesk_Animator_Codec) | ✅ | ✅ | ffmpeg | `AASC` at 24 bits a pixel, bottom-up. Other depths and top-down heights refused | [MultimediaWiki AASC](https://wiki.multimedia.cx/index.php/Autodesk_Animator_Codec) |
| [FLIC](https://en.wikipedia.org/wiki/FLIC_(file_format)) | ⚠️ | ✅ | ffmpeg | `FLIC`; palettised 8-bit over a canvas kept between packets. Sub-chunk types outside {4, 7, 11, 12, 13, 15, 16, 18} and other depths refused. The encoder is lossless and accepts `Indexed8` only: the first packet states the complete `COLOR256` palette, later packets state changed palette spans, black pictures use `BLACK`, changed pictures use `BRUN`, and unchanged index planes carry only any palette update. True-colour input is refused rather than quantised | [MultimediaWiki FLIC](https://wiki.multimedia.cx/index.php/Flic_Video) |
| [Q-Team QPEG](https://wiki.multimedia.cx/index.php/QPEG) | ✅ | ✅ | ffmpeg | `QPEG`, `Q1.0`, `Q1.1`; palettised 8-bit bottom-up, with run-length, skip, fill-table and block motion coding. The encoder writes canonical `QPEG` from `Indexed8` pictures, preserving the stream palette and choosing between complete frames and smaller no-motion deltas. It searches for no motion and references no fill table, so a moving picture costs more than a codec-aware encoder would spend; direct colour, alpha and palette changes are refused rather than quantised | [MultimediaWiki QPEG](https://wiki.multimedia.cx/index.php/QPEG) |
| [ASUS V1](https://wiki.multimedia.cx/index.php/Asus_Video) | ✅ | ✅ | ffmpeg | `ASV1`; intra-only 4:2:0 DCT, per-file quantiser from stream private data. The encoder writes `ASV1` at quantiser 8, every picture whole and on its own — the format has no prediction between pictures — and drops the twenty-four block positions the End-Of-Block-terminated coding states must be nought. Lossy by construction, so only a flat picture comes back exactly. Ten streams written here (34x18 to 352x288, four of them not a whole number of macroblocks; flat colour, `testsrc2`, `mandelbrot`, `smptebars`, `rgbtestsrc`, a gradient and uniform noise), 82 frames, were decoded by ffmpeg 9.0.1: every frame accepted with no message, and its 4:2:0 planes differ from this package's own decode of the same bytes by at most one level — 14838 samples of 2869164, none by more than one, which is the two inverse transforms rounding apart. Against the pictures that went in the mean peak signal-to-noise ratio is 37.2 dB, and every one of the ten files is smaller than ffmpeg's own encoder writes at the same quantiser | [MultimediaWiki Asus Video](https://wiki.multimedia.cx/index.php/Asus_Video) |
| [ASUS V2](https://wiki.multimedia.cx/index.php/Asus_Video) | ✅ | ✅ | ffmpeg | `ASV2`; ASV1's macroblock under a reversed bit order and an explicit coefficient-group count. The encoder writes `ASV2` at quantiser 16 — the same effective step ASV1's 8 gives against the coarser scale — and reaches all sixteen coefficient groups, so unlike ASV1 no block position is unreachable. Lossy by construction, so only a flat picture comes back exactly. Ten streams written here (the same sources and sizes as ASV1), 82 frames, were decoded by ffmpeg 9.0.1: every frame accepted with no message, and its 4:2:0 planes differ from this package's own decode of the same bytes by at most one level — 15233 samples of 2869164. Against the pictures that went in the mean peak signal-to-noise ratio is 42.8 dB, and every one of the ten files is smaller than ffmpeg's own encoder writes at the same quantiser | [MultimediaWiki Asus Video](https://wiki.multimedia.cx/index.php/Asus_Video) |
| [Creative YUV (CYUV)](https://wiki.multimedia.cx/index.php/Creative_YUV) | ✅ | ✅ | ffmpeg | `cyuv`; decoder accepts the 4:1:1 per-row difference coding and the real uncompressed UYVY 4:2:2 packet shape. The encoder deliberately writes the latter, bottom-up, because the published differential format specifies how to read its three per-frame tables but not how an encoder chooses them. Widths that are not a whole number of four-pixel groups refused | [Ferguson, `cyuv.txt`](https://multimedia.cx/mirror/cyuv.txt) |
| [Cirrus Logic AccuPak (CLJR)](https://wiki.multimedia.cx/index.php/Cirrus_Logic_AccuPak) | ✅ | ✅ | ffmpeg | `CLJR`; 4:1:1 quantised into one 32-bit word per four pixels. Widths not divisible by four refused, as the format's own encoder refuses them. The encoder writes the same quantised word, with the reference's dither offset held fixed so a picture always codes to the same bytes | [MultimediaWiki AccuPak](https://wiki.multimedia.cx/index.php/Cirrus_Logic_AccuPak) |
| [HuffYUV / FFVHUFF](https://en.wikipedia.org/wiki/Huffyuv) | ⚠️ | ✅ | ffmpeg | `HFYU`, `FFVH` at 8 bits, with left, gradient and median prediction. Interlaced 4:2:0 under median prediction, deeper samplings, unknown prediction methods and original-HuffYUV streams carrying no stream description refused. The encoder writes progressive 8-bit frames with the tables in the stream description, in the 4:2:2 interleaved, packed colour and planar layouts; interlaced frames, per-frame tables and 4:2:0 are refused | [MultimediaWiki HuffYUV](https://wiki.multimedia.cx/index.php/HuffYUV) |
| [Ut Video](https://wiki.multimedia.cx/index.php/Ut_Video) | ⚠️ | ✅ | ffmpeg | `ULRG`, `ULRA`, `ULY0`, `ULY2`, `ULY4`, `ULH0`, `ULH2`, `ULH4`. The Pro (`UQ*`) and T2 (`UM*`) codes are accepted by the registry and then refused by name, as are interlaced frames and the fsemedian entropy mode. The encoder writes the 8-bit `ULRG`, `ULRA`, `ULY0`, `ULY2` and `ULY4` layouts, single-frame prediction, one slice per frame by default | [Ut Video](https://github.com/umezawatakeshi/utvideo) |
| MagicYUV | ⚠️ | ✅ | none | The 8-bit `M0*`, `M2*`, `M4*` and `M8*` codes. `MAGY`, `M8GA` (grey with alpha) and the 10/12/14-bit codes are accepted and then refused by name. No neutral overview of this format is published. The encoder writes the six 8-bit layouts that have a picture type to come from — `M8G0`, `M8RG`, `M8RA`, `M8Y4`, `M8Y2`, `M8Y0` — with median prediction by default | [MagicYUV](https://www.magicyuv.com/) |
| [LCL ZLIB](https://wiki.multimedia.cx/index.php/Lossless_Codec_Libraries) | ⚠️ | ✅ | ffmpeg | `ZLIB`; RGB24 only. The YUV image types, the multithread flag and the PNG-filter flag are refused — the format's own specification leaves each unstated. The encoder writes every picture as one complete zlib stream of RGB24 rows, bottom row first, so every packet is a key frame | [Togni, LCL codecs](https://wiki.multimedia.cx/index.php/Lossless_Codec_Libraries) |
| [LCL MSZH](https://wiki.multimedia.cx/index.php/Lossless_Codec_Libraries) | ⚠️ | ✅ | ffmpeg | `MSZH`; RGB24 only, in the MSZH and uncompressed modes, including two-section packets. The back-reference parser is adapted from FFmpeg's LGPL-2.1-or-later decoder. The encoder writes single-section bottom-up RGB24 with DWORD-padded rows, using MSZH back-references where they shrink the frame and the reference decoder's equal-size raw form otherwise. The undocumented YUV byte orders and the multithread split fields are left alone rather than guessed | [FFmpeg `lcldec.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/lcldec.c) |
| [LOCO](https://wiki.multimedia.cx/index.php/LOCO) | ⚠️ | ✅ | ffmpeg | `LOCO`; RGB, RGBA, YUV 4:2:2 and 4:2:0. Odd-width 4:2:2, odd-sized 4:2:0 and unknown colour modes refused; odd-width RGB is repaired with the historical decoder rotation. The encoder writes version-1 lossless RGB24 and RGBA32, every packet a key frame; odd-width RGB writing is refused because that compatibility transform is non-invertible, while RGBA may be odd-width. The decoder is adapted from FFmpeg's LGPL-2.1-or-later implementation; the writer independently derives the inverse bitstream and is checked by ffmpeg | [FFmpeg `loco.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/loco.c) |
| [Canopus Lossless Codec](https://wiki.multimedia.cx/index.php/Canopus_Lossless) | ⚠️ | — | — | `CLLC`; YUV 4:2:2, RGB24 and ARGB. Odd-width 4:2:2 and the blocked YUV coding refused — the latter is unimplemented in the reference decoder too. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `cllc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/cllc.c) |
| [VBLE Lossless Codec](https://wiki.multimedia.cx/index.php/VBLE) | ✅ | — | — | `VBLE`; YUV 4:2:0 at codec version 1. Odd-sized pictures refused rather than given a guessed fringe. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `vble.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/vble.c) |
| [MidiVid Archive Codec](https://wiki.multimedia.cx/index.php/Midivid) | ✅ | — | — | `MVHA`; YUV 4:2:2 from either the zlib or the Huffman payload. Odd widths and undefined packet types refused. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `mvha.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mvha.c) |
| [ZeroCodec](https://wiki.multimedia.cx/index.php/ZeroCodec) | ⚠️ | — | — | `ZECO`; packed 4:2:2 at 16 bits a pixel, the one layout a real recording exists for. Other depths and odd widths refused | [MultimediaWiki ZeroCodec](https://wiki.multimedia.cx/index.php/ZeroCodec) |
| [TechSmith Screen Capture](https://wiki.multimedia.cx/index.php/TechSmith_Screen_Capture_Codec) | ✅ | — | — | `tscc`; 8-bit palettised, 16, 24 and 32 bits. Other depths refused. A packet that is not a zlib stream is an unchanged frame, not an error | [MultimediaWiki TSCC](https://wiki.multimedia.cx/index.php/TechSmith_Screen_Capture_Codec) |
| [CamStudio Screen Codec](https://en.wikipedia.org/wiki/CamStudio) | ✅ | — | — | `CSCD`; 16, 24 and 32 bits behind LZO or zlib. 8-bit palettised refused, as the format has no palettised mode | [MultimediaWiki CamStudio](https://wiki.multimedia.cx/index.php/CamStudio_Screen_Codec) |
| [Flash Screen Video](https://wiki.multimedia.cx/index.php/Flash_screen_video) | ✅ | ✅ | ffmpeg | `FSV1`; a grid of independently zlib-compressed blocks. Mid-stream geometry changes refused. The encoder writes 64x64 cells, each its own zlib stream, sending only the cells whose bytes changed and a full key frame every twelfth | [SWF File Format Specification](https://open-flash.github.io/mirrors/swf-spec-19.pdf) |
| [Flash Screen Video 2](https://wiki.multimedia.cx/index.php/Flash_screen_video) | ⚠️ | — | — | `FSV2` at 24-bit RGB and the 15-bit depth. `HasIFrameImage`, `ZlibPrimeCompressCurrent` and key-frame blocks that do not cover their cell refused — the specification describes none of the three well enough to check a reading | [SWF File Format Specification](https://open-flash.github.io/mirrors/swf-spec-19.pdf) |
| [Zip Motion Blocks Video](https://wiki.multimedia.cx/index.php/DosBox_Capture_Codec) | ⚠️ | ✅ | ffmpeg | `ZMBV` version 0.1, uncompressed and zlib, with the zlib dictionary carried across packets. A stream opening on an interframe, and the video formats no encoder writes, refused. The encoder writes version 0.1 at 8, 15, 16 and 32 bits with the zlib dictionary carried across packets, cutting each frame into the format's own blocks | [MultimediaWiki ZMBV](https://wiki.multimedia.cx/index.php/DosBox_Capture_Codec) |
| [MS Screen 1](https://wiki.multimedia.cx/index.php/Microsoft_Screen_Codec) | ✅ | — | — | `MSS1`; palettised arithmetic-coded screen video. Pictures wider or taller than 4096 are rejected as invalid. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `mss1.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mss1.c) |
| Mandsoft / Screen Recorder Gold | ✅ | — | — | `MSCC`, `SRGC`; 8, 16, 24 and 32 bits. Other depths, and an indexed stream whose AVI carries no palette, refused. Adapted from FFmpeg's LGPL-2.1-or-later decoder. No neutral overview of this format is published | [FFmpeg `mscc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mscc.c) |
| MatchWare Screen Capture Codec | ✅ | — | — | `MWSC`; 24-bit BGR run-length walk inside one zlib stream. No neutral overview of this codec is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `mwsc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mwsc.c) |
| RemotelyAnywhere Screen Capture | ⚠️ | — | — | `RASC`; PAL8, RGB555 and BGR0 with cursor overlay. Delta and MOVE compression type 2 refused — unimplemented in the reference decoder too. No neutral overview is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `rasc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/rasc.c) |
| innoHeim/Rsupport Screen Capture Codec | ✅ | — | — | `RSCC`, `ISCC`; 8, 16, 24 and 32 bits, tiles applied to a persistent picture. Other depths, and an indexed stream with no static palette, refused. No neutral overview is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `rscc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/rscc.c) |
| Screenpresso | ✅ | — | — | `SPV1`; full frames and additive deltas behind zlib. No neutral overview is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `screenpresso.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/screenpresso.c) |
| WinCAM Motion Video | ✅ | — | — | `WCMV`; 16, 24 and 32 bits as rectangular updates onto a persistent picture. Other depths refused. No neutral overview is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `wcmv.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/wcmv.c) |
| VMware Screen Codec / VMware Video | ⚠️ | — | — | `VMnc`; RFB rectangles at 16 and 32 stored bits (a container stating 24 means 32), raw and Hextile, with the AND/XOR cursor sprite composited onto the returned picture and never into the reference canvas. 8-bit indexed screens refused — the codec packet carries no palette to draw them with. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `vmnc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/vmnc.c) |
| TDSC | ✅ | — | — | `TDSC`; raw and JPEG tiles onto a persistent BGR24 canvas behind zlib, with monochrome, BGRA and RGBA cursors composited onto the returned picture only. JPEG tiles go through the image package's own JPEG reader. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `tdsc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/tdsc.c) |
| [Uncompressed (BI_RGB)](https://en.wikipedia.org/wiki/BMP_file_format) | ✅ | ✅ | ffmpeg | Codec tag 0 / `vfw`; device-independent bitmap pixel arrays at 1, 4, 8, 16, 24 and 32 bits, laid out by the `BITMAPINFOHEADER` the container carried. Other depths refused. The encoder writes the same pixel arrays at 8, 16, 24 and 32 bits with the `BITMAPINFOHEADER` a container needs | [BITMAPINFOHEADER](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader) |
| [Planar raw YUV](https://wiki.multimedia.cx/index.php/YUV4MPEG2) | ⚠️ | ✅ | none | `YUV `, `rawvideo`; the YUV4MPEG2 chroma tokens `mono`, `420`, `422` and `444` at 8, 10, 12 and 16 bits. `420mpeg2` and `420paldv` refused — their chroma siting has no faithful pixel format here. The encoder writes the same tokens back | [MultimediaWiki YUV4MPEG2](https://wiki.multimedia.cx/index.php/YUV4MPEG2) |
| [Uncompressed 4:2:2 10-bit (v210)](https://wiki.multimedia.cx/index.php/V210) | ✅ | ✅ | ffmpeg | 10-bit 4:2:2, six luma samples to a sixteen-byte group, rows padded to 128 bytes. The encoder writes the same groups and the same 128-byte row padding | [MultimediaWiki v210](https://wiki.multimedia.cx/index.php/V210) |
| Uncompressed 4:2:2 10-bit (012v) | ⚠️ | ✅ | ffmpeg | 10-bit 4:2:2; v210's group layout with the row length taken from the packet rather than padded. A stride that is not a whole number of groups is refused. Layout recovered by measurement. The encoder writes the same groups and pads no row, which is what makes the row length recoverable from the packet | — |
| [Uncompressed RGB 10-bit (r210)](https://wiki.multimedia.cx/index.php/R210) | ✅ | ✅ | ffmpeg | 10-bit RGB, one big-endian word a pixel, rows padded to 256 bytes. Decoded straight to `Rgb30` with no reduction to eight bits. The encoder writes the bit string ffmpeg's own `r210enc.c` writes, cross-checked against it | [MultimediaWiki r210](https://wiki.multimedia.cx/index.php/R210) |
| [AJA Kona 10-bit RGB (r10k)](https://wiki.multimedia.cx/index.php/AJA_Kona_10-bit_RGB_Codec) | ✅ | ✅ | ffmpeg | AJA Kona 10-bit RGB; a different bit arrangement from r210 and no row padding at all. The encoder writes the same words, unpadded | [MultimediaWiki AJA Kona](https://wiki.multimedia.cx/index.php/AJA_Kona_10-bit_RGB_Codec) |
| [Uncompressed YUV 4:1:1 (y41p)](https://fourcc.org/pixel-format/yuv-y41p/) | ✅ | ✅ | ffmpeg | 4:1:1, twelve bytes to eight luma samples, rows coded bottom row first. Widths that are not a whole number of eight-pixel groups refused. Layout recovered by measurement. The encoder writes the same twelve-byte groups, bottom row first | [FOURCC y41p](https://fourcc.org/pixel-format/yuv-y41p/) |
| Uncompressed 4:4:4 (v308) | ✅ | ✅ | ffmpeg | 4:4:4, three bytes a pixel (V, Y, U) with no padding. Layout recovered by measurement — no page describes this tag. The encoder writes the same three bytes a pixel | — |
| Uncompressed 4:4:4 with alpha (v408) | ✅ | ✅ | ffmpeg | 4:4:4 with alpha, four bytes a pixel (U, Y, V, A) with no padding. Layout recovered by measurement. The encoder writes the same four bytes a pixel, alpha carried through untouched | — |
| [Uncompressed 4:4:4:4 (ayuv)](https://fourcc.org/pixel-format/yuv-ayuv/) | ✅ | ✅ | ffmpeg | 4:4:4:4, four bytes a pixel stored V, U, Y, alpha — the reverse of what the name spells. Layout recovered by measurement. The encoder writes the same four bytes a pixel, alpha carried through untouched | [FOURCC AYUV](https://fourcc.org/pixel-format/yuv-ayuv/) |
| [Uncompressed packed 4:2:2 (YUY2)](https://fourcc.org/pixel-format/yuv-yuy2/) | ✅ | ✅ | ffmpeg | 4:2:2, two pixels to four bytes stored Y, Cb, Y, Cr, no padding. Odd widths refused. Decoded to `Yuv422P8` samples rather than converted to colour. The encoder writes the same four bytes, byte for byte what ffmpeg's `yuyv422` writes | [FOURCC YUY2](https://fourcc.org/pixel-format/yuv-yuy2/) |
| [Uncompressed packed 4:2:2 (YVYU)](https://fourcc.org/pixel-format/yuv-yvyu/) | ✅ | ✅ | ffmpeg | YUY2 with the chroma pair exchanged: Y, Cr, Y, Cb. Odd widths refused. The encoder writes the same four bytes, byte for byte what ffmpeg's `yvyu422` writes | [FOURCC YVYU](https://fourcc.org/pixel-format/yuv-yvyu/) |
| [Uncompressed packed 4:2:2 (UYVY)](https://fourcc.org/pixel-format/yuv-uyvy/) | ✅ | ✅ | ffmpeg | YUY2 rotated by one byte: Cb, Y, Cr, Y. Odd widths refused. The encoder writes the same four bytes, byte for byte what ffmpeg's `uyvy422` writes | [FOURCC UYVY](https://fourcc.org/pixel-format/yuv-uyvy/) |
| [Uncompressed packed 4:2:2 (VYUY)](https://docs.kernel.org/userspace-api/media/v4l/pixfmt-packed-yuv.html) | ✅ | ✅ | ffmpeg | UYVY with the chroma pair exchanged: Cr, Y, Cb, Y, as V4L2 documents the code. Odd widths refused. ffmpeg maps this tag onto `yuyv422` instead, so it is not an oracle for it; the packing is checked against its `uyvy422` writing of the chroma-exchanged picture, which is the same bytes | [V4L2 packed YUV](https://docs.kernel.org/userspace-api/media/v4l/pixfmt-packed-yuv.html) |
| [Uncompressed planar 4:2:0 (YV12)](https://fourcc.org/pixel-format/yuv-yv12/) | ✅ | ✅ | ffmpeg | Luma plane, then the whole Cr plane, then the whole Cb plane. Odd widths and heights refused. Decoded to `Yuv420P8` samples rather than converted to colour. ffmpeg's raw-video encoder writes I420's plane order under this tag while its decoder exchanges the planes; what is written here is what the code states | [FOURCC YV12](https://fourcc.org/pixel-format/yuv-yv12/) |
| [Uncompressed planar 4:2:0 (I420)](https://fourcc.org/pixel-format/yuv-i420/) | ✅ | ✅ | ffmpeg | Luma plane, then the whole Cb plane, then the whole Cr plane — ffmpeg's `yuv420p`. Odd widths and heights refused. The encoder writes the same three planes | [FOURCC I420](https://fourcc.org/pixel-format/yuv-i420/) |
| [Uncompressed planar 4:2:0 (IYUV)](https://learn.microsoft.com/windows/win32/medfound/recommended-8-bit-yuv-formats-for-video-rendering) | ✅ | ✅ | ffmpeg | Byte for byte I420 under a second name, kept apart so a stream can be written back under whichever code it stated. Odd widths and heights refused | [Microsoft 8-bit YUV formats](https://learn.microsoft.com/windows/win32/medfound/recommended-8-bit-yuv-formats-for-video-rendering) |
| [Uncompressed semi-planar 4:2:0 (NV12)](https://fourcc.org/pixel-format/yuv-nv12/) | ✅ | ✅ | ffmpeg | Luma plane, then one interleaved chroma plane, Cb byte first. Odd widths and heights refused. The encoder writes the same two planes | [FOURCC NV12](https://fourcc.org/pixel-format/yuv-nv12/) |
| [Uncompressed semi-planar 4:2:0 (NV21)](https://fourcc.org/pixel-format/yuv-nv21/) | ✅ | ✅ | ffmpeg | NV12 with the two bytes of every chroma pair exchanged, Cr byte first. Odd widths and heights refused. The encoder writes the same two planes | [FOURCC NV21](https://fourcc.org/pixel-format/yuv-nv21/) |
| [Uncompressed grey (Y800)](https://fourcc.org/pixel-format/yuv-y800/) | ✅ | ✅ | ffmpeg | One byte a pixel, luma only, no padding. Any picture size, odd dimensions included — the one of these layouts with no chroma grid to divide. Decoded to `Gray8` with the samples unscaled. The encoder writes the same bytes | [FOURCC Y800](https://fourcc.org/pixel-format/yuv-y800/) |
| [id RoQ](https://wiki.multimedia.cx/index.php/RoQ) | ⚠️ | ✅ | ffmpeg | `RoQV`; quadtree vector quantisation with motion compensation over two picture buffers. The `RoQ_JPEG` superset chunk and mid-stream size changes refused. The encoder writes the same quadtree — one packet per picture carrying the `INFO`, `QUAD_CODEBOOK` and `QUAD_VQ` chunks it is made of — sizing a codebook per picture and pricing every block between a skip, a motion vector, one 4x4 cell doubled and subdivision. It is lossy because RoQ is: at most 256 cells of four luminances and one chrominance pair paint a whole picture, so only a picture the codebook can hold outright comes back exactly. A picture whose sides are not a whole number of 16-pixel macroblocks is refused by name rather than padded to one | [MultimediaWiki RoQ](https://wiki.multimedia.cx/index.php/RoQ) |
| [Interplay Video](https://wiki.multimedia.cx/index.php/Interplay_Video) | ⚠️ | — | — | `IMVE`; 8-bit palettised, all sixteen block encodings, motion compensation included. The true-colour `INIT_VIDEO_BUFFERS` mode refused | [MultimediaWiki Interplay Video](https://wiki.multimedia.cx/index.php/Interplay_Video) |
| id Cinematic Video | ✅ | — | — | `IDCV`; order-1 static Huffman over an already-palettised 8-bit picture, 256 trees. No neutral overview of this codec is published | — |
| [Westwood VQA Video](https://wiki.multimedia.cx/index.php/VQA) | ⚠️ | — | — | `WSVQ` format version 2, 8-bit palettised, with the codebook rationed across eight pictures. Other format versions and the 15-bit colour form refused | [MultimediaWiki VQA](https://wiki.multimedia.cx/index.php/VQA) |
| [Electronic Arts CMV](https://wiki.multimedia.cx/index.php/Electronic_Arts_CMV) | ⚠️ | — | — | `cmv `; palettised 4x4 block replacement against the last two pictures. Pictures that are not a whole number of blocks, and mid-stream size changes, refused | [MultimediaWiki EA CMV](https://wiki.multimedia.cx/index.php/Electronic_Arts_CMV) |
| [Commodore CDXL Video](https://en.wikipedia.org/wiki/CDXL) | ✅ | — | — | `CDXL`; bit-planar pictures through a twelve-bit palette or through Hold-And-Modify | [MultimediaWiki CDXL](https://wiki.multimedia.cx/index.php/CDXL) |
| [IFF ANIM Video](https://en.wikipedia.org/wiki/ANIM) | ⚠️ | — | — | `ANIM`; compression method 5 (Byte Vertical Delta) only, palettised or Hold-And-Modify. The other four methods the specification names are not decoded | [Amiga ANIM IFF](https://wiki.amigaos.net/wiki/ANIM_IFF_CEL_Animations) |
| [Brute Force & Ignorance Video](https://wiki.multimedia.cx/index.php/BFI) | ✅ | — | — | `BFIV`; palettised 8-bit with literal runs, back-references, carried runs and fills | [MultimediaWiki BFI](https://wiki.multimedia.cx/index.php/BFI) |
| [Sierra VMD Video](https://wiki.multimedia.cx/index.php/VMD) | ⚠️ | — | — | `VMDV` codec version 2, 8-bit palettised, painted one rectangle at a time. New-palette frames, empty rectangles, LZ rectangles without the preload marker and unknown rendering methods refused | [MultimediaWiki VMD](https://wiki.multimedia.cx/index.php/VMD) |
| [Smacker Video](https://wiki.multimedia.cx/index.php/Smacker) | ✅ | — | — | `SMK2` and `SMK4`; 8-bit palettised 4x4 blocks read through four Huffman tables the file states once and every frame shares, with the running palette resolved here rather than in the demuxer. A picture that is not a whole number of 4x4 blocks refuses, as does a stream stating none of its four tables. The composition of those four tables — the piece RAD's own description leaves out, and what this codec sat undecoded on — is adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `smacker.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/smacker.c) |
| [Escape 124](https://wiki.multimedia.cx/index.php/Escape_124) | ⚠️ | — | — | ARMovie/RPL codec id 124; 8x8 superblocks, so dimensions not divisible by eight are refused rather than left fringed. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `escape124.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/escape124.c) |
| [Eidos Escape 130](https://wiki.multimedia.cx/index.php/Escape_130) | ✅ | — | — | ARMovie/RPL codec id 130; 2x2 blocks, so a picture must be a whole number of them | [MultimediaWiki Escape 130](https://wiki.multimedia.cx/index.php/Escape_130) |

**Oracle** names the program outside this repository that has read what the encoder writes —
[ffmpeg](https://ffmpeg.org/) throughout, since it is the one other implementation of most of these
formats. `—` is a codec with no encoder, so there is nothing for anything to have read. `none` is an
encoder nothing else has looked at: MagicYUV, because this ffmpeg build ships that codec's encoder
and not its decoder, and Planar raw YUV, because no container here names it with a code ffmpeg maps
to a decoder. The claim is declared by `[VerifiedBy]` on the encoder type, carried into
`VideoCodecEncoderEntry.VerifiedBy` by the registry generator, and held to what it says by
[`EncoderOracleTests`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Tests/Hawkynt.FileFormats.Video.Tests/EncoderOracleTests.cs):
the encoder writes a short clip, it is muxed into a container that carries the code, and ffmpeg has
to decode a frame of it back at the size that went in. A pass there is weaker than the sample-exact
comparisons recorded in
[`codec-notes.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-notes.md)
and stronger than this package's own decoder agreeing with its own encoder, which is what the column
exists to stop being mistaken for evidence.

### Not supported, and why

The package claims the whole domain, so a codec it does not decode is a stated result and not a
silence. Each of the codecs below was investigated against real files and every description that
could be found, and each stopped somewhere specific. None of them is refused for want of effort, and
none is waiting on a decision — they are waiting on a fact that is not published anywhere. Where each
one stops precisely, and what would change the answer, is in
[`codec-investigations.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-investigations.md).

Three walls account for nearly all of them. **The tables are in the binary, not in the files** — the
smallest frame in a corpus cannot hold a quantisation table, so a table the format needs and never
transmits can only come from an implementation. **The only description is a paraphrase of a decoder**
— a wiki page whose function names, typos and constants match the decoder that predates it is not an
independent source, and building on it makes a provenance problem out of a reverse-engineering one.
**There is no corpus at all** — no encoder exists to make a file and no archive carries one, so
nothing could be verified even with a description in hand.

| Codec | Why it is not decoded |
| --- | --- |
| TrueMotion 1 (`DUCK`) | The tables are in the binary, at its limit — a zero-byte frame. Every table is in the decoder |
| TrueMotion 2 (`TM20`) | Two thirds recovered from the bitstream; the remaining delta tables are not in the files |
| On2 VP4 | Structure published and verified against On2's own `vp4vfw.dll` frame by frame, but the per-component motion-vector Huffman codes are stored as branches in the binary and printed nowhere |
| On2 VP5 | No published bitstream description at all, and it shares VP6's wall besides |
| On2 VP6 (`VP60`/`VP61`/`VP62`) | Every table **is** published and every one was transcribed and checked; the decode still desynchronises on the third magnitude bit of the first coefficient block, and no variant tried fixes it without breaking others |
| On2 VP7 | On2's own specification names its reference decoder's `quant_common.c` and `findnearmv.c` at exactly the two points — the dequantisation tables and the interframe motion-vector census — where it declines to print what they hold |
| Sorenson Video 1 (`SVQ1`) | The one technical document explains the algorithm fully and then cites three FFmpeg source files as where the codebook is, instead of printing it |
| Sorenson Video 3 (`SVQ3`) | Everything by which it departs from H.264 is described only by an uncited page that attributes its one sourced fact to `svq3.c` |
| Windows Media Video 7 (`WMV1`) | Its run-level, DC and motion-vector tables are MS-MPEG4v3's own undocumented ones, tied to them by two shared escape constants; without them only empty macroblocks can be located in a corpus |
| Windows Media Video 8 (`WMV2`) | WMV1's whole wall plus its own undocumented joint type/CBP table |
| Microsoft Screen 2 (`MSS2`) | The absent source MS Screen 1 had, plus embedded WMV9 rectangles whose boundaries within a packet are stated nowhere |
| Lagarith (`LAGS`) | The wrapper comes out completely; the range coder inside it is defined by one implementation's floating-point rounding rather than by anything written down, so even a reference decode is not a sound oracle |
| NewTek SpeedHQ | Container, framing and DC coding match ISO/IEC 13818-2 exactly; some unknown number of AC codewords are reassigned, and the one source printing the reassignment copied it from FFmpeg |
| Canopus HQ, HQA, HQX | The vendor's papers are marketing; the one bitstream write-up is the decoder author's own account of reverse-engineering the format |
| Dxtory | The one page states only that frames hold YV12 blocks behind a 16-byte header, and was written by the decoder's author on the day of his decoder commit |
| HuffYUV MT (`HYMT`) | The multithreaded slice table is described only by the fork's own GPL source, which is both an implementation and licence-incompatible with this package |
| Brooktree ProSumer (`BT20`) | One sentence, written four years after the decoder, names a codebook needed for delta-pair replacement and prints not one entry of it |
| SheerVideo | The properly sourced frame-format document names but never prints a dozen static VLC codesets and seed predictors — and because they are static rather than transmitted, no file can ever reveal them |
| YLC | Complete and well-sourced except for one clause naming a 225-entry constant table of YUYV quads that is printed nowhere; one 100-frame sample cannot demonstrate every entry was exercised |
| Go2Meeting (`G2M`) | The one detailed page states no provenance, over three coding paths including a JPEG hybrid — too much to build on an unconfirmed paraphrase |
| ScreenPressor (`SCPR`) | The only descriptions found are two third-party reimplementations, which is more implementations, and one sample file |
| TechSmith Screen Codec 2 (`TSCC2`) | The only technical write-up is by the decoder's own author, about writing the decoder |
| FM Screen Capture (`FMVC`) | Every technical fact on its page was added five years after the decoder, and even then names its two compression types as LZ77 variants without stating either one's coding |
| Electronic Arts TGQ, TQI, MAD | The shared inverse transform and zigzag are undocumented; the wiki page's edit history shows its own IDCT description being replaced by a link to the author's FFmpeg source |
| Electronic Arts TGV | Four of five intra statement forms decode correctly against real files; the three-byte form's bit layout does not match the published formula and no single-field adjustment reaches the right copy offset without breaking the rest |
| Deluxe Paint Animation (`ANM`) | The container is fully documented by EA's own manual; the RunSkipDump opcodes exist only in EA's unpublished program source, which is the format owner's code and still not licensed for transcription |
| Chronomaster DFA | The only description reads as a transcription of a working decoder — C-shaped pseudocode with unexplained bit-level tie-breaks and no citation for any of its six chunk algorithms |
| 8088flex TMV | No description of any kind, and the single sample is truncated before its own last frame |

### Adapted from somebody else's code, and named

Seventeen decoders are adaptations of FFmpeg's own LGPL-2.1-or-later decoders rather than
implementations from a published description: Escape 124, Indeo 4 and 5's tables, LCL MSZH's
back-reference parser, LOCO, Canopus Lossless, Matrox M101, VBLE, MidiVid Archive, MS Screen 1,
RemotelyAnywhere, MSCC, MWSC, RSCC, Screenpresso, WinCAM, VMware Screen Codec, TDSC and Smacker. Sixteen of the encoders are as well:
Apple Graphics, Apple Video, Cinepak's bitstream, CLJR, DV, FFV1, Flash Screen Video, Hap's block
compression, HuffYUV, LCL ZLIB, MagicYUV, Microsoft RLE, Microsoft Video 1's mode decision,
QuickTime Animation, Ut Video and ZMBV. Hap's is the one of those not under the LGPL: FFmpeg's
`texturedspenc.c` carries its own MIT grant and states it derives from public-domain code, and the
notice beside it says so. The ASUS, FLIC, H.261, id RoQ and Apple ProRes encoders are not among them at
all: those were written from published descriptions. The Indeo 4 encoder is not among them either: it
is derived from the format behaviour this package's own decoder and shared IVI layer already express,
and ffmpeg stands over it as an interoperability oracle rather than as encoder source. ProRes takes FFmpeg's quantisation matrices,
which RDD 36 prints nowhere, but constants are not expression and the notice beside it records
where they came from. Every one of those files
carries the original author and the licence notice it came under; LGPL-2.1-or-later permits
redistribution under this package's LGPL-3.0-or-later.

Each of those sixteen sat among the undecoded above, and for the same reason the entries there give:
the missing piece existed nowhere but an implementation. Reading a licence-compatible implementation
is what closed them. The entries recording why they could not be closed the other way are kept,
because that reasoning still holds for everything reached without one.

FFmpeg is not the only source. The H.264 decoder's CABAC engine — `Codecs/H264/`, the arithmetic
decoder and its context tables — is adapted from
[OxideAV/oxideav-h264](https://github.com/OxideAV/oxideav-h264)'s `src/cabac.rs` and
`src/cabac_ctx.rs`, under the MIT licence. A verbatim copy of that licence and the statement of what
was taken sit beside the code in
[`Codecs/H264/THIRD-PARTY-NOTICE.OxideAV.txt`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/Codecs/H264/THIRD-PARTY-NOTICE.OxideAV.txt),
which is where the licence requires it to be rather than only here.

## 🚀 Quick start

```csharp
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

var file = new FileInfo("movie.mkv");

// Detect and inspect the container without decoding frames.
VideoFormat format = VideoFormatRegistry.Detect(file);
IReadOnlyList<MediaStreamInfo> streams = VideoFormatRegistry.ReadStreams(file);
VideoMetadata metadata = VideoFormatRegistry.ReadMetadata(file);

Console.WriteLine($"{format}: {streams.Count} stream(s)");

// Decode the first video stream lazily.
foreach (var frame in VideoFormatRegistry.DecodeFrames(file)) {
  RawImage image = frame.Image;
  Console.WriteLine($"{image.Width}x{image.Height} pts={frame.PresentationTimestamp}");
}
```

### Demux without decoding

```csharp
var data = File.ReadAllBytes("movie.avi");
var streams = VideoFormatRegistry.ReadStreams(data);

foreach (var packet in VideoFormatRegistry.ReadPackets(data)) {
  // CodedPacket remains compressed; no decoder was constructed.
}
```

### Mux without encoding

```csharp
using FileFormat.H264Video;

var source = H264VideoContainer.FromBytes(File.ReadAllBytes("source.h264"));
var streams = H264VideoContainer.Streams(source);
var packets = H264VideoContainer.ReadPackets(source);

byte[] remuxed = VideoIO.Mux<H264VideoWriter>(streams, packets, H264VideoContainer.Metadata(source));
File.WriteAllBytes("copy.h264", remuxed);
```

### Select a decoder explicitly

```csharp
var videoStream = streams.First(s => s.Kind == MediaStreamKind.Video);

if (VideoFormatRegistry.CanDecode(videoStream)) {
  IVideoFrameDecoder decoder = VideoFormatRegistry.CreateDecoder(videoStream);
  // Feed coded packets for that stream through the decoder as needed.
}
```

## 📚 Core API

| Member | Purpose |
| --- | --- |
| `VideoFormatRegistry.AllFormats` | Enumerate registered containers. |
| `VideoFormatRegistry.AllCodecs` | Enumerate registered decoders. |
| `VideoFormatRegistry.AllEncoders` | Enumerate registered encoders. |
| `Detect(ReadOnlyMemory<byte>)` / `Detect(FileInfo)` | Detect container by content. |
| `ByExtension(string)` | Find container candidates by extension. |
| `ByMimeType(string)` | Find a container by MIME type. |
| `ReadStreams(byte[] / FileInfo)` | Read declared media streams. |
| `ReadPackets(byte[], ...)` | Lazily demux coded packets. |
| `ReadMetadata(byte[] / FileInfo)` | Read container metadata. |
| `VideoIO.CreateWriter<TWriter>(...)` | Create a statically dispatched container writer. |
| `VideoIO.Mux<TWriter>(...)` | Packet-level mux/remux without decoding or encoding. |
| `CanDecode(MediaStreamInfo)` | Test whether any registered codec accepts a stream. |
| `CreateDecoder(MediaStreamInfo)` | Create the matching frame decoder or throw a named refusal. |
| `CanEncode(MediaStreamInfo)` | Test whether any registered encoder writes a stream's codec. |
| `CreateEncoder(MediaStreamInfo)` | Create the matching packet encoder or throw a named refusal. |
| `DecodeFrames(byte[] / FileInfo, ...)` | Convenience path combining demux + decode. |

### Four contracts

| Responsibility | Contract | Owns |
| --- | --- | --- |
| Demux | `IVideoContainerReader<T>` | packet locations, timestamps, stream metadata |
| Decode | `IVideoCodecDecoder<T>` | codec bitstream → decoded frame |
| Encode | `IVideoCodecEncoder<T>` | decoded frame → coded packet |
| Mux | `IVideoContainerWriter<T>` | coded packets → container layout |

## 🏗️ Architecture

A few container rules explain much of the implementation shape:

- **MP4/MOV/3GP** share one ISO-base-media parser. Packet boundaries are reconstructed from sample tables rather than scanned out of `mdat`; QuickTime `cmov` compressed headers are expanded before the same atom parser runs.
- **Matroska/WebM** share EBML structure. Block lacing is unpacked into individual coded packets and unknown-length live-stream elements are terminated structurally.
- **MPEG Program Stream** reassembles PES payloads and then cuts them at elementary-stream picture boundaries; PES packet size is not assumed to equal picture size.
- **MPEG Transport Stream** reconstructs PES/program state from TS packets rather than exposing transport packet boundaries as codec packets.
- **Motion JPEG** uses the JPEG parser from the image package rather than byte-searching for `FF D9`, which can occur inside entropy-coded data or embedded thumbnails.
- **PlayStation STR** remuxing writes actual 2352-byte Mode-2 sectors, including Form-1 EDC/ECC and Form-2 EDC; parity is not left blank merely because the demuxer does not need it.
- **RoQ and STR** retain rare packet-local framing state in `CodedPacket.ContainerPrivateData` when that state is required to reproduce the container but is not part of the codec payload itself.
- **Flash Video** writes back what it reads: the title, author, album, encoder, creation date, duration and every annotation cross a remux as an `onMetaData` script tag. A reader that finds a field and a writer with nowhere to put it are not a round trip, they are a quiet deletion.
- **Ogg** has no metadata area of its own, so the title, artist, album, encoder, date and annotations are written into the comment header of every Theora, Vorbis and Opus bitstream in the file, which is where the mappings put them. The vendor string stays with the library that encoded the stream, and the framing bit is written for Vorbis alone, as its specification and no other asks for one.

Those rules exist because “find a familiar marker and split there” works on demo files and fails on real media. Detailed validation notes live next to the relevant readers and in [`codec-notes.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-notes.md).

## 📚 API reference

<!-- API:BEGIN generated by Hawkynt/RepositoryTemplate/package-readme — edit the XML docs in source, not here -->

Every public and protected member of all 492 types, generated from the built assembly and its XML documentation, is in [REFERENCE.md](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/REFERENCE.md).

<!-- API:END -->

## 🔌 Dependencies

| Dependency | Role |
| --- | --- |
| [`Hawkynt.FileFormats.Images`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Images/README.md) | JPEG/bitmap readers and shared decoded-frame ecosystem. |
| `FileFormat.Core` | `RawImage`, `CodedPacket`, `MediaStreamInfo`, `DecodedFrame`, and format infrastructure. |
| `FileFormat.Core.Generators` | Compile-time format metadata generation. |
| `FileFormat.Registry.Generator` | Compile-time video registry metadata generation. |
| `FrameworkExtensions.Backports` | Framework backports used by the package. |

## ⚠️ Limitations

- Metadata crosses a remux only where the destination container has somewhere to put it. AVI, Matroska, MP4, ASF, RealMedia, RPL, FLV and Ogg write it back; the raw byte streams and most of the game formats have no metadata area at all and drop it, which is a property of those formats rather than of this package. Ogg carries the fields its comment headers define and not a duration, which it states as the granule position of its last page instead.
- Mux support is packet-level remuxing, not codec transcoding. A writer may require container-specific stream description bytes, timing geometry, fragment offsets, or packet-private state when those cannot be reconstructed from coded payload alone.
- H.264/H.265 raw-stream muxers accept Annex B packets only; length-prefixed MP4/QuickTime packet representations are refused rather than silently written as invalid byte streams.
- MP4/QuickTime muxing requires a complete sample entry in `CodecPrivateData` for codecs whose configuration cannot be synthesized safely; missing codec configuration is refused rather than guessed.
- Large RealVideo pictures require preserved slice offsets when they must be split across 16-bit RealMedia packet lengths, and RoQ sound requires its original predictor argument.
- Several advanced codecs intentionally implement well-defined subsets (for example H.264 progressive 8-bit 4:2:0, HEVC without the range and screen-content extensions, and VC-1 Simple/Main intra pictures). Every row marked ⚠️ in the codec table names its own subset. Unsupported profiles/features are refused by name rather than silently misdecoded.
- Codec support is more precise than a single green check can express; consult [`codec-notes.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-notes.md) before relying on a profile/level/feature not named in this README.
- Encoding is a smaller domain than decoding on purpose: 70 codecs of the 100 read can also be written. Most are lossless; twenty-seven are not, and each says so in its own row. Motion JPEG writes baseline JPEG, whose loss is a matter of degree, and DV's fixed-length frames, Microsoft Video 1's two-colours-to-a-block coding, Cinepak's vector quantisation, Microsoft's MPEG-4 transform, the ASUS codecs' quantised DCT, Apple Video's 15-bit blocks, H.261's, H.263's, MPEG-1 video's, MPEG-2 video's, MPEG-4 Part 2's, VP3's, VP8's, VC-1's, Theora's and Apple ProRes's quantised transforms, CineForm's quantised wavelet, H.264's and H.265's 4:2:0 conversion, Hap's texture blocks, id RoQ's vector quantisation and Intel Indeo 4's YVU9 chroma subsampling have no lossless form at all — a picture that codec can hold exactly comes back exactly, and one it cannot does not. What is still not written back is the modern lossy codecs: a stream read as H.264 cannot be written back as H.264.
- Video correctness depends on real-world packetization as much as codec math. The project therefore validates packet counts, sizes, timestamps, and key-frame flags against external tools where samples are available.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE).
