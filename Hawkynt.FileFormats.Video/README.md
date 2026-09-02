# Hawkynt.FileFormats.Video

[![NuGet](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Video.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Video/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Target](https://img.shields.io/badge/target-net8.0-blue)

> Pure-C# video handling, with demuxing, decoding, encoding and muxing kept as separate contracts so
> packet-level remuxing never has to become a decode/re-encode by accident. The package claims the
> WHOLE domain — every video container and codec, not a selection of it. Where one is missing or only
> partly supported that is a tracked gap, recorded in [Format / codec support](#-format--codec-support)
> below and codec by codec in [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

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
- Registry-based container/codec dispatch instead of container-specific decoder plumbing in callers.
- Packet boundaries are reconstructed according to each container's indexing/lacing/PES rules rather than guessed from byte patterns.
- Packet-level remuxing preserves coded bytes and refuses source representations whose required container state cannot be reproduced honestly.

## 🧩 Format / codec support

Both tables are complete: every container the package reads or writes, and every codec it registers, has a row. The codec table states each codec's implemented scope and what it refuses; where a description came from, how far it was verified, and what is deliberately *not* implemented and why are in [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

### Container support

| Container / stream format | Extensions | Demux | Mux | Reference |
| --- | --- | :---: | :---: | --- |
| [Advanced Systems Format (ASF)](https://en.wikipedia.org/wiki/Advanced_Systems_Format) | `.asf`, `.wmv`, `.wma`, `.wm`, `.wmx`, `.asx` | ✅ | ✅ | [Microsoft ASF overview](https://learn.microsoft.com/windows/win32/wmformat/overview-of-the-asf-format) |
| [AVI](https://en.wikipedia.org/wiki/Audio_Video_Interleave) | `.avi` | ✅ | ✅ | [Microsoft AVI RIFF reference](https://learn.microsoft.com/windows/win32/directshow/avi-riff-file-reference) |
| [Flash Video](https://en.wikipedia.org/wiki/Flash_Video) | `.flv`, `.f4v` | ✅ | ✅ | [FLV format description](https://www.loc.gov/preservation/digital/formats/fdd/fdd000131.shtml) |
| [ISO Base Media / MP4 / QuickTime](https://en.wikipedia.org/wiki/ISO_base_media_file_format) | `.mp4`, `.m4v`, `.mov`, `.qt`, `.3gp`, `.3g2`, `.m4a` | ✅ | ✅ | [MP4RA](https://mp4ra.org/) / [Apple QuickTime File Format](https://developer.apple.com/documentation/quicktime-file-format) |
| [Matroska](https://en.wikipedia.org/wiki/Matroska) / [WebM](https://en.wikipedia.org/wiki/WebM) | `.mkv`, `.mka`, `.mks`, `.mk3d`, `.webm` | ✅ | ✅ | [Matroska elements](https://www.matroska.org/technical/elements.html) / [WebM container](https://www.webmproject.org/docs/container/) |
| [H.264 Annex B byte stream](https://en.wikipedia.org/wiki/Advanced_Video_Coding) | `.264`, `.h264`, `.avc`, `.x264` | ✅ | ✅ | [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264) |
| [H.265 / HEVC Annex B byte stream](https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding) | `.265`, `.h265`, `.hevc`, `.x265` | ✅ | ✅ | [ITU-T H.265](https://www.itu.int/rec/T-REC-H.265) |
| [MPEG Program Stream](https://en.wikipedia.org/wiki/MPEG_program_stream) | `.mpg`, `.mpeg`, `.vob`, `.m2p`, `.m2ps` | ✅ | ✅ | [MPEG-2 Systems](https://mpeg.chiariglione.org/standards/mpeg-2/systems) |
| [MPEG Transport Stream](https://en.wikipedia.org/wiki/MPEG_transport_stream) | `.ts`, `.m2ts`, `.mts`, `.m2t`, `.tsv` | ✅ | ✅ | [MPEG-2 Systems](https://mpeg.chiariglione.org/standards/mpeg-2/systems) |
| [Motion JPEG stream](https://en.wikipedia.org/wiki/Motion_JPEG) | `.mjpg`, `.mjpeg` | ✅ | ✅ | [JPEG / ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [MPEG elementary video stream](https://en.wikipedia.org/wiki/Elementary_stream) | `.m1v`, `.m2v`, `.mpv`, `.mpeg1video`, `.mpeg2video` | ✅ | ✅ | [MPEG-1 Video](https://mpeg.chiariglione.org/standards/mpeg-1/video) / [MPEG-2 Video](https://mpeg.chiariglione.org/standards/mpeg-2/video) |
| [Ogg](https://en.wikipedia.org/wiki/Ogg) | `.ogg`, `.ogv`, `.oga`, `.ogx`, `.opus`, `.spx` | ✅ | ✅ | [RFC 3533](https://www.rfc-editor.org/rfc/rfc3533) |
| [RealMedia](https://en.wikipedia.org/wiki/RealMedia) | `.rm`, `.rmvb`, `.ra`, `.rmj`, `.rms` | ✅ | ✅ | [MultimediaWiki RealMedia](https://wiki.multimedia.cx/index.php/RealMedia) |
| [Autodesk FLIC](https://en.wikipedia.org/wiki/FLIC_(file_format)) | `.fli`, `.flc`, `.flx` | ✅ | ✅ | [MultimediaWiki FLIC](https://wiki.multimedia.cx/index.php/FLIC) |
| [id RoQ](https://en.wikipedia.org/wiki/RoQ) | `.roq` | ✅ | ✅ | [MultimediaWiki RoQ](https://wiki.multimedia.cx/index.php/RoQ) |
| [Interplay MVE](https://wiki.multimedia.cx/index.php/Interplay_MVE) | `.mve` | ✅ | ✅ | [MultimediaWiki MVE](https://wiki.multimedia.cx/index.php/Interplay_MVE) |
| [id Cinematic](https://wiki.multimedia.cx/index.php/Id_Cinematic) | `.cin` | ✅ | ✅ | [MultimediaWiki CIN](https://wiki.multimedia.cx/index.php/Id_Cinematic) |
| [Westwood VQA](https://wiki.multimedia.cx/index.php/Westwood_VQA) | `.vqa` | ✅ | ✅ | [MultimediaWiki VQA](https://wiki.multimedia.cx/index.php/Westwood_VQA) |
| [Smacker](https://en.wikipedia.org/wiki/Smacker_video) | `.smk` | ✅ | ✅ | [RAD Game Tools](https://www.radgametools.com/smkmain.htm) |
| [Electronic Arts Multimedia](https://wiki.multimedia.cx/index.php/Electronic_Arts_Formats) | `.wve`, `.cmv`, `.tgv`, `.uv`, `.uv2` | ✅ | ✅ | [MultimediaWiki EA formats](https://wiki.multimedia.cx/index.php/Electronic_Arts_Formats) |
| [BFI](https://wiki.multimedia.cx/index.php/Brute_Force_%26_Ignorance) | `.bfi` | ✅ | ✅ | [MultimediaWiki BFI](https://wiki.multimedia.cx/index.php/Brute_Force_%26_Ignorance) |
| [Commodore CDXL](https://en.wikipedia.org/wiki/CDXL) | `.cdxl` | ✅ | ✅ | [MultimediaWiki CDXL](https://wiki.multimedia.cx/index.php/CDXL) |
| [IFF ANIM](https://en.wikipedia.org/wiki/ANIM) | `.anim`, `.iff` | ✅ | ✅ | [Amiga ANIM IFF](https://wiki.amigaos.net/wiki/ANIM_IFF_Animation) |
| [Sierra VMD](https://wiki.multimedia.cx/index.php/Sierra_VMD) | `.vmd` | ✅ | ✅ | [MultimediaWiki VMD](https://wiki.multimedia.cx/index.php/Sierra_VMD) |
| [PlayStation STR](https://wiki.multimedia.cx/index.php/PlayStation_STR) | `.str` | ✅ | ✅ | [MultimediaWiki STR](https://wiki.multimedia.cx/index.php/PlayStation_STR) |
| [ARMovie/RPL](https://wiki.multimedia.cx/index.php/ARMovie) | `.rpl` | ✅ | ✅ | [MultimediaWiki ARMovie](https://wiki.multimedia.cx/index.php/ARMovie) |

### Codec support

Every codec the package registers has a row. `Decode` is what
[`VideoFormatRegistry.CreateDecoder`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/VideoFormatRegistry.cs)
builds; `Encode` is a public `IVideoCodecEncoder<T>`, which exactly two codecs have.

**⚠️ means the decoder refuses something the format itself defines** — a profile, depth, variant or
mode a conforming encoder may write — and the Notes column says which. **✅ means every layout the
format defines is decoded**; such a codec still refuses malformed input, undefined field values and
forms no encoder produces, because a plausible wrong picture is worse than a refusal. No codec
silently misdecodes what it will not read. Codec-by-codec provenance and measurement notes are in
[`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

| Codec | Decode | Encode | Implemented scope / note | Reference |
| --- | :---: | :---: | --- | --- |
| [Motion JPEG](https://en.wikipedia.org/wiki/Motion_JPEG) | ✅ | ✅ | `MJPG`, `jpeg`, `V_MJPEG`; each packet one whole JPEG. The encoder writes baseline JPEG, every packet a key frame | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [Apple Motion JPEG-B](https://en.wikipedia.org/wiki/Motion_JPEG) | ✅ | — | `mjpb`; baseline JPEG behind a 48-byte field header, one or two fields a packet. The header layout is recovered by measurement, not published | [MultimediaWiki MJPEG](https://wiki.multimedia.cx/index.php/MJPEG) |
| [Avid AVRn](https://wiki.multimedia.cx/index.php/Motion_JPEG) | ✅ | — | `AVRn`; baseline JPEG, but the container's picture size is trusted over the JPEG frame header's | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [MPEG-1 Video](https://en.wikipedia.org/wiki/MPEG-1) | ⚠️ | — | I, P and B pictures. D pictures (2.4.2.8) refused by name | [ISO/IEC 11172-2](https://www.iso.org/standard/22411.html) |
| [MPEG-2 Video](https://en.wikipedia.org/wiki/H.262/MPEG-2_Part_2) | ⚠️ | — | Frame pictures at 4:2:0 and 4:2:2, including field DCT and field motion compensation. Field pictures, dual-prime prediction, 4:4:4 High profile and the scalability extensions refused | [ITU-T H.262](https://www.itu.int/rec/T-REC-H.262) |
| [MPEG-4 Part 2](https://en.wikipedia.org/wiki/MPEG-4_Part_2) | ⚠️ | — | Rectangular, progressive, 8-bit 4:2:0 I/P/B. Quarter-sample vectors, sprites/GMC, interlace, OBMC, data partitioning, scalability, shape coding, newpred, reduced resolution and the complexity-estimation header each refused where signalled | [ISO/IEC 14496-2](https://www.iso.org/standard/39259.html) |
| [H.261](https://en.wikipedia.org/wiki/H.261) | ⚠️ | — | QCIF and CIF, clauses 3 and 4 entire, in-loop filter included. Annex D still-image transmission refused | [ITU-T H.261](https://www.itu.int/rec/T-REC-H.261) |
| [H.263](https://en.wikipedia.org/wiki/H.263) | ⚠️ | — | Baseline (clauses 5-6) and Sorenson Spark. Annexes C, D, E, F, G and T, and the extended PTYPE of 5.1.4, each refused where signalled | [ITU-T H.263](https://www.itu.int/rec/T-REC-H.263) |
| [H.264 / AVC](https://en.wikipedia.org/wiki/Advanced_Video_Coding) | ⚠️ | — | Progressive 8-bit 4:2:0; CAVLC and CABAC I/P/B slices, High-profile 8x8 transform and scaling lists, long-term references, weighted and direct prediction. 4:2:2/4:4:4, depths above 8 bits, interlace/MBAFF, FMO, data partitioning, SVC/MVC and lossless bypass refused | [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264) |
| [H.265 / HEVC](https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding) | ⚠️ | — | Main profile, 8-bit 4:2:0; intra and inter slices, reference management, CABAC, weighted prediction, scaling lists, deblocking, SAO, tiles and dependent slices. PCM coding units, the format range extensions, screen-content coding, multilayer/3D and separate colour planes refused | [ITU-T H.265](https://www.itu.int/rec/T-REC-H.265) |
| [VC-1 / WMV 9](https://en.wikipedia.org/wiki/VC-1) | ⚠️ | — | `WMV3`, Simple and Main profile **intra pictures only**; sequence header read from container private data. Predicted, bidirectional and skipped pictures, Advanced profile (`WVC1`), MULTIRES, RANGERED and LOOPFILTER each refused by name | [SMPTE ST 421](https://ieeexplore.ieee.org/document/7290900) |
| [Microsoft MPEG-4 v2](https://en.wikipedia.org/wiki/MPEG-4_Part_2) | ⚠️ | — | `MP42`, intra and predicted pictures. Versions 1 (`MPG4`, `DIV1`) and 3 (`MP43`, `DIV3`, `AP41`) are accepted by the registry and then refused by name — their run-level, DC and motion-vector tables are unpublished | [ISO/IEC 14496-2](https://www.iso.org/standard/39259.html) |
| [RealVideo 1](https://en.wikipedia.org/wiki/RealVideo) | ⚠️ | — | `RV10`, `RV13` at bitstream revision 0; H.263 macroblock layer under RealVideo's own slice header. PB-frames refused. RealVideo 2/3/4 are not claimed at all, so they reach the registry's own "no codec decodes this" refusal | [ITU-T H.263](https://www.itu.int/rec/T-REC-H.263) |
| [On2 VP3](https://en.wikipedia.org/wiki/VP3) | ⚠️ | — | VP3.1 (`VP31`, `VP32`) entire. `VP30` is accepted and then refused: a VP3.0 key frame cannot be read with VP3.1's rules at any bit offset | [Theora specification, Appendix B](https://www.theora.org/doc/Theora.pdf) |
| [VP8](https://en.wikipedia.org/wiki/VP8) | ✅ | — | RFC 6386 entire. Reserved bitstream versions and the reserved colour-space/clamping fields refused | [RFC 6386](https://www.rfc-editor.org/rfc/rfc6386) |
| [VP9](https://en.wikipedia.org/wiki/VP9) | ✅ | — | Profiles 0-3: 8-, 10- and 12-bit, 4:2:0 and the non-4:2:0 layouts, and the full-range sRGB/GBR representation | [WebM VP9](https://www.webmproject.org/vp9/) |
| [Theora](https://en.wikipedia.org/wiki/Theora) | ✅ | — | Xiph.Org Theora I, all three pixel formats. Bitstream versions other than 3.2, the reserved pixel format and set reserved bits refused | [Theora specification](https://www.theora.org/doc/Theora.pdf) |
| [FFV1](https://en.wikipedia.org/wiki/FFV1) | ⚠️ | — | Versions 0, 1 and 3, both entropy coders, slices and checksums; 8-bit samples only. Version 2 and deeper samplings refused | [RFC 9043](https://www.rfc-editor.org/rfc/rfc9043) |
| [Apple ProRes](https://en.wikipedia.org/wiki/Apple_ProRes) | ⚠️ | — | `apco`, `apcs`, `apcn`, `apch` at 4:2:2 and `ap4h`, `ap4x` at 4:4:4; bitstream versions 0 and 1. Reserved chroma formats, reserved interlace mode and reserved alpha types refused | [Apple ProRes white paper](https://www.apple.com/final-cut-pro/docs/Apple_ProRes_White_Paper.pdf) |
| [Avid DNxHD / DNxHR](https://en.wikipedia.org/wiki/DNxHD_codec) | ⚠️ | — | SMPTE VC-3 header versions 1-3, progressive 4:2:2 and 4:4:4. Interlaced frames, 4:2:0, alpha-bearing compression IDs and RGB-mode macroblocks refused | [SMPTE VC-3 overview](https://ieeexplore.ieee.org/document/7290708) |
| [GoPro CineForm](https://en.wikipedia.org/wiki/CineForm) | ⚠️ | — | SMPTE VC-5 three-channel layouts: 10-bit 4:2:2 YUV and 12-bit RGB. Alpha-bearing channel counts and lowpass precisions other than 16 bits refused | [LOC CineForm overview](https://www.loc.gov/preservation/digital/formats/fdd/fdd000458.shtml) |
| Hap | ⚠️ | — | `Hap1`, `Hap5`, `HapY`, `HapM`, `HapA`, `Hap7`, `HapH`. Texture formats and second-stage compressors the codec does not know, and image combinations Hap does not define, refused. Neither Wikipedia nor MultimediaWiki carries a page for this format; the format's own documentation is the overview | [Vidvox Hap `HapVideoDRAFT.md`](https://github.com/Vidvox/hap/blob/master/documentation/HapVideoDRAFT.md) |
| Matrox Uncompressed SD | ✅ | — | `M101`; 8- and 10-bit 4:2:2 read from the 24-byte Matrox AVI trailer. Odd widths and other sample depths refused. Adapted from FFmpeg's LGPL-2.1-or-later decoder. No neutral overview of this format is published | [FFmpeg `m101.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/m101.c) |
| Avid 1:1 10-bit RGB | ✅ | — | `AVrp`; little-endian 10-bit RGB word, rows padded to 64-pixel blocks. Word and padding recovered by measurement — nothing about this layout is published | — |
| [Avid Meridien Uncompressed](https://wiki.multimedia.cx/index.php/AVUI) | ⚠️ | — | `AVUI`; UYVY 4:2:2 behind a fixed run of blank lines. Only 720x486 and 720x576 accepted, the two geometries the format's own encoder writes | [MultimediaWiki AVUI](https://wiki.multimedia.cx/index.php/AVUI) |
| [Microsoft Video 1](https://wiki.multimedia.cx/index.php/Microsoft_Video_1) | ✅ | — | `CRAM`, `MSVC`, `WHAM` at 8-bit palettised and 16-bit 5-5-5. Other depths, and pictures that are not a whole number of 4x4 blocks, refused | [MultimediaWiki](https://wiki.multimedia.cx/index.php/Microsoft_Video_1) |
| [Microsoft RLE](https://wiki.multimedia.cx/index.php/Microsoft_RLE) | ✅ | — | `MRLE`, `BI_RLE8`, `BI_RLE4`; 4- and 8-bit bottom-up frames with delta and skip escapes. Top-down heights and other depths refused | [MultimediaWiki Microsoft RLE](https://wiki.multimedia.cx/index.php/Microsoft_RLE) |
| [Cinepak](https://en.wikipedia.org/wiki/Cinepak) | ✅ | — | `cvid`, `CVID`; QuickTime and AVI alike. Strips that are not a whole number of 4x4 blocks, unknown chunk types and mid-stream size changes refused | [MultimediaWiki Cinepak](https://wiki.multimedia.cx/index.php/Cinepak) |
| [QuickTime Animation](https://en.wikipedia.org/wiki/QuickTime_Animation) | ✅ | — | `rle `; depths 1, 2, 4, 8, 16, 24 and 32, plus greyscale 33, 34, 36 and 40. A palettised stream with no colour table is refused rather than drawn through a guessed palette | [MultimediaWiki QuickTime RLE](https://wiki.multimedia.cx/index.php/Apple_QuickTime_RLE) |
| [Apple Video (RPZA)](https://en.wikipedia.org/wiki/Apple_Video) | ✅ | — | `rpza` in QuickTime, `azpr` in AVI; 15-bit RGB vector quantisation over 4x4 blocks | [MultimediaWiki Apple RPZA](https://wiki.multimedia.cx/index.php/Apple_RPZA) |
| [QuickTime Graphics (SMC)](https://en.wikipedia.org/wiki/QuickTime_Graphics) | ✅ | — | `smc `; 8-bit palettised only. The colour table comes from the sample description, or from the QuickTime default where the stream names none; a stream naming a system colour resource by number is refused | [MultimediaWiki Apple SMC](https://wiki.multimedia.cx/index.php/Apple_SMC) |
| [Apple Planar RGB (8BPS)](https://wiki.multimedia.cx/index.php/8BPS) | ⚠️ | — | `8BPS` at 8-bit palettised, 24-bit RGB and 32-bit RGB with alpha. A named system colour resource is refused rather than substituted | [MultimediaWiki 8BPS](https://wiki.multimedia.cx/index.php/8BPS) |
| [Autodesk Animator Codec](https://wiki.multimedia.cx/index.php/Autodesk_Animator_Codec) | ✅ | — | `AASC` at 24 bits a pixel, bottom-up. Other depths and top-down heights refused | [MultimediaWiki AASC](https://wiki.multimedia.cx/index.php/Autodesk_Animator_Codec) |
| [Autodesk FLIC](https://en.wikipedia.org/wiki/FLIC_(file_format)) | ⚠️ | — | `FLIC`; palettised 8-bit over a canvas kept between packets. Sub-chunk types outside {4, 7, 11, 12, 13, 15, 16, 18} and other depths refused | [MultimediaWiki FLIC](https://wiki.multimedia.cx/index.php/Flic_Video) |
| [Q-Team QPEG](https://wiki.multimedia.cx/index.php/QPEG) | ✅ | — | `QPEG`, `Q1.0`, `Q1.1`; palettised 8-bit bottom-up, with run-length, skip, fill-table and block motion coding | [MultimediaWiki QPEG](https://wiki.multimedia.cx/index.php/QPEG) |
| [ASUS V1](https://wiki.multimedia.cx/index.php/Asus_Video) | ✅ | — | `ASV1`; intra-only 4:2:0 DCT, per-file quantiser from stream private data | [MultimediaWiki Asus Video](https://wiki.multimedia.cx/index.php/Asus_Video) |
| [ASUS V2](https://wiki.multimedia.cx/index.php/Asus_Video) | ✅ | — | `ASV2`; ASV1's macroblock under a reversed bit order and an explicit coefficient-group count | [MultimediaWiki Asus Video](https://wiki.multimedia.cx/index.php/Asus_Video) |
| [Creative YUV](https://wiki.multimedia.cx/index.php/Creative_YUV) | ✅ | — | `cyuv`; 4:1:1 per-row difference coding against three per-frame tables. Widths that are not a whole number of four-pixel groups refused | [Ferguson, `cyuv.txt`](https://multimedia.cx/mirror/cyuv.txt) |
| [Cirrus Logic AccuPak](https://wiki.multimedia.cx/index.php/Cirrus_Logic_AccuPak) | ✅ | — | `CLJR`; 4:1:1 quantised into one 32-bit word per four pixels. Widths not divisible by four refused, as the format's own encoder refuses them | [MultimediaWiki AccuPak](https://wiki.multimedia.cx/index.php/Cirrus_Logic_AccuPak) |
| [HuffYUV / FFVHUFF](https://en.wikipedia.org/wiki/Huffyuv) | ⚠️ | — | `HFYU`, `FFVH` at 8 bits, with left, gradient and median prediction. Interlaced 4:2:0 under median prediction, deeper samplings, unknown prediction methods and original-HuffYUV streams carrying no stream description refused | [MultimediaWiki HuffYUV](https://wiki.multimedia.cx/index.php/HuffYUV) |
| [Ut Video](https://wiki.multimedia.cx/index.php/Ut_Video) | ⚠️ | — | `ULRG`, `ULRA`, `ULY0`, `ULY2`, `ULY4`, `ULH0`, `ULH2`, `ULH4`. The Pro (`UQ*`) and T2 (`UM*`) codes are accepted by the registry and then refused by name, as are interlaced frames and the fsemedian entropy mode | [Ut Video](https://github.com/umezawatakeshi/utvideo) |
| MagicYUV | ⚠️ | — | The 8-bit `M0*`, `M2*`, `M4*` and `M8*` codes. `MAGY`, `M8GA` (grey with alpha) and the 10/12/14-bit codes are accepted and then refused by name. No neutral overview of this format is published | [MagicYUV](https://www.magicyuv.com/) |
| [LCL ZLIB](https://wiki.multimedia.cx/index.php/Lossless_Codec_Libraries) | ⚠️ | — | `ZLIB`; RGB24 only. The YUV image types, the multithread flag and the PNG-filter flag are refused — the format's own specification leaves each unstated | [Togni, LCL codecs](https://wiki.multimedia.cx/index.php/Lossless_Codec_Libraries) |
| [LCL MSZH](https://wiki.multimedia.cx/index.php/Lossless_Codec_Libraries) | ⚠️ | — | `MSZH`; RGB24 only, in the MSZH and uncompressed modes. The back-reference parser is adapted from FFmpeg's LGPL-2.1-or-later decoder, the published description leaving it as a placeholder | [FFmpeg `lcldec.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/lcldec.c) |
| [LOCO](https://wiki.multimedia.cx/index.php/LOCO) | ⚠️ | — | `LOCO`; RGB, RGBA, YUV 4:2:2 and 4:2:0. Odd-width 4:2:2 and RGB, odd-sized 4:2:0 and unknown colour modes refused. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `loco.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/loco.c) |
| [Canopus Lossless](https://wiki.multimedia.cx/index.php/Canopus_Lossless) | ⚠️ | — | `CLLC`; YUV 4:2:2, RGB24 and ARGB. Odd-width 4:2:2 and the blocked YUV coding refused — the latter is unimplemented in the reference decoder too. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `cllc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/cllc.c) |
| [VBLE](https://wiki.multimedia.cx/index.php/VBLE) | ✅ | — | `VBLE`; YUV 4:2:0 at codec version 1. Odd-sized pictures refused rather than given a guessed fringe. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `vble.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/vble.c) |
| [MidiVid Archive](https://wiki.multimedia.cx/index.php/Midivid) | ✅ | — | `MVHA`; YUV 4:2:2 from either the zlib or the Huffman payload. Odd widths and undefined packet types refused. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `mvha.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mvha.c) |
| [ZeroCodec](https://wiki.multimedia.cx/index.php/ZeroCodec) | ⚠️ | — | `ZECO`; packed 4:2:2 at 16 bits a pixel, the one layout a real recording exists for. Other depths and odd widths refused | [MultimediaWiki ZeroCodec](https://wiki.multimedia.cx/index.php/ZeroCodec) |
| [TechSmith Screen Capture](https://wiki.multimedia.cx/index.php/TechSmith_Screen_Capture_Codec) | ✅ | — | `tscc`; 8-bit palettised, 16, 24 and 32 bits. Other depths refused. A packet that is not a zlib stream is an unchanged frame, not an error | [MultimediaWiki TSCC](https://wiki.multimedia.cx/index.php/TechSmith_Screen_Capture_Codec) |
| [CamStudio](https://en.wikipedia.org/wiki/CamStudio) | ✅ | — | `CSCD`; 16, 24 and 32 bits behind LZO or zlib. 8-bit palettised refused, as the format has no palettised mode | [MultimediaWiki CamStudio](https://wiki.multimedia.cx/index.php/CamStudio_Screen_Codec) |
| [Flash Screen Video](https://wiki.multimedia.cx/index.php/Flash_screen_video) | ✅ | — | `FSV1`; a grid of independently zlib-compressed blocks. Mid-stream geometry changes refused | [SWF File Format Specification](https://open-flash.github.io/mirrors/swf-spec-19.pdf) |
| [Flash Screen Video 2](https://wiki.multimedia.cx/index.php/Flash_screen_video) | ⚠️ | — | `FSV2` at 24-bit RGB and the 15-bit depth. `HasIFrameImage`, `ZlibPrimeCompressCurrent` and key-frame blocks that do not cover their cell refused — the specification describes none of the three well enough to check a reading | [SWF File Format Specification](https://open-flash.github.io/mirrors/swf-spec-19.pdf) |
| [Zip Motion Blocks Video](https://wiki.multimedia.cx/index.php/DosBox_Capture_Codec) | ⚠️ | — | `ZMBV` version 0.1, uncompressed and zlib, with the zlib dictionary carried across packets. A stream opening on an interframe, and the video formats no encoder writes, refused | [MultimediaWiki ZMBV](https://wiki.multimedia.cx/index.php/DosBox_Capture_Codec) |
| [MS Screen 1](https://wiki.multimedia.cx/index.php/Microsoft_Screen_Codec) | ✅ | — | `MSS1`; palettised arithmetic-coded screen video. Pictures wider or taller than 4096 are rejected as invalid. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `mss1.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mss1.c) |
| Mandsoft / Screen Recorder Gold | ✅ | — | `MSCC`, `SRGC`; 8, 16, 24 and 32 bits. Other depths, and an indexed stream whose AVI carries no palette, refused. Adapted from FFmpeg's LGPL-2.1-or-later decoder. No neutral overview of this format is published | [FFmpeg `mscc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mscc.c) |
| MatchWare Screen Capture | ✅ | — | `MWSC`; 24-bit BGR run-length walk inside one zlib stream. No neutral overview of this format is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `mwsc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mwsc.c) |
| RemotelyAnywhere Screen Capture | ⚠️ | — | `RASC`; PAL8, RGB555 and BGR0 with cursor overlay. Delta and MOVE compression type 2 refused — unimplemented in the reference decoder too. No neutral overview is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `rasc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/rasc.c) |
| innoHeim / Rsupport Screen Capture | ✅ | — | `RSCC`, `ISCC`; 8, 16, 24 and 32 bits, tiles applied to a persistent picture. Other depths, and an indexed stream with no static palette, refused. No neutral overview is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `rscc.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/rscc.c) |
| Screenpresso | ✅ | — | `SPV1`; full frames and additive deltas behind zlib. No neutral overview of this format is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `screenpresso.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/screenpresso.c) |
| WinCAM Motion Video | ✅ | — | `WCMV`; 16, 24 and 32 bits as rectangular updates onto a persistent picture. Other depths refused. No neutral overview is published. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `wcmv.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/wcmv.c) |
| [Uncompressed (BI_RGB)](https://en.wikipedia.org/wiki/BMP_file_format) | ✅ | — | Codec tag 0 / `vfw`; device-independent bitmap pixel arrays at 1, 4, 8, 16, 24 and 32 bits, laid out by the `BITMAPINFOHEADER` the container carried. Other depths refused | [BITMAPINFOHEADER](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader) |
| [Planar raw YUV](https://wiki.multimedia.cx/index.php/YUV4MPEG2) | ⚠️ | ✅ | `YUV `, `rawvideo`; the YUV4MPEG2 chroma tokens `mono`, `420`, `422` and `444` at 8, 10, 12 and 16 bits. `420mpeg2` and `420paldv` refused — their chroma siting has no faithful pixel format here. The encoder writes the same tokens back | [MultimediaWiki YUV4MPEG2](https://wiki.multimedia.cx/index.php/YUV4MPEG2) |
| [v210](https://wiki.multimedia.cx/index.php/V210) | ✅ | — | 10-bit 4:2:2, six luma samples to a sixteen-byte group, rows padded to 128 bytes | [MultimediaWiki v210](https://wiki.multimedia.cx/index.php/V210) |
| 012v | ⚠️ | — | 10-bit 4:2:2; v210's group layout with the row length taken from the packet rather than padded. A stride that is not a whole number of groups is refused. Layout recovered by measurement | — |
| [r210](https://wiki.multimedia.cx/index.php/R210) | ✅ | — | 10-bit RGB, one big-endian word a pixel, rows padded to 256 bytes. Decoded straight to `Rgb30` with no reduction to eight bits | [MultimediaWiki r210](https://wiki.multimedia.cx/index.php/R210) |
| [r10k](https://wiki.multimedia.cx/index.php/AJA_Kona_10-bit_RGB_Codec) | ✅ | — | AJA Kona 10-bit RGB; a different bit arrangement from r210 and no row padding at all | [MultimediaWiki AJA Kona](https://wiki.multimedia.cx/index.php/AJA_Kona_10-bit_RGB_Codec) |
| [y41p](https://fourcc.org/pixel-format/yuv-y41p/) | ✅ | — | 4:1:1, twelve bytes to eight luma samples, rows coded bottom row first. Widths that are not a whole number of eight-pixel groups refused. Layout recovered by measurement | [FOURCC y41p](https://fourcc.org/pixel-format/yuv-y41p/) |
| v308 | ✅ | — | 4:4:4, three bytes a pixel (V, Y, U) with no padding. Layout recovered by measurement — no page describes this tag | — |
| v408 | ✅ | — | 4:4:4 with alpha, four bytes a pixel (U, Y, V, A) with no padding. Layout recovered by measurement | — |
| [ayuv](https://fourcc.org/pixel-format/yuv-ayuv/) | ✅ | — | 4:4:4:4, four bytes a pixel stored V, U, Y, alpha — the reverse of what the name spells. Layout recovered by measurement | [FOURCC AYUV](https://fourcc.org/pixel-format/yuv-ayuv/) |
| [id RoQ](https://wiki.multimedia.cx/index.php/RoQ) | ⚠️ | — | `RoQV`; quadtree vector quantisation with motion compensation over two picture buffers. The `RoQ_JPEG` superset chunk and mid-stream size changes refused | [MultimediaWiki RoQ](https://wiki.multimedia.cx/index.php/RoQ) |
| [Interplay Video](https://wiki.multimedia.cx/index.php/Interplay_Video) | ⚠️ | — | `IMVE`; 8-bit palettised, all sixteen block encodings, motion compensation included. The true-colour `INIT_VIDEO_BUFFERS` mode refused | [MultimediaWiki Interplay Video](https://wiki.multimedia.cx/index.php/Interplay_Video) |
| id Cinematic | ✅ | — | `IDCV`; order-1 static Huffman over an already-palettised 8-bit picture, 256 trees. No neutral overview of this codec is published | — |
| [Westwood VQA](https://wiki.multimedia.cx/index.php/VQA) | ⚠️ | — | `WSVQ` format version 2, 8-bit palettised, with the codebook rationed across eight pictures. Other format versions and the 15-bit colour form refused | [MultimediaWiki VQA](https://wiki.multimedia.cx/index.php/VQA) |
| [Electronic Arts CMV](https://wiki.multimedia.cx/index.php/Electronic_Arts_CMV) | ⚠️ | — | `cmv `; palettised 4x4 block replacement against the last two pictures. Pictures that are not a whole number of blocks, and mid-stream size changes, refused | [MultimediaWiki EA CMV](https://wiki.multimedia.cx/index.php/Electronic_Arts_CMV) |
| [Commodore CDXL](https://en.wikipedia.org/wiki/CDXL) | ✅ | — | `CDXL`; bit-planar pictures through a twelve-bit palette or through Hold-And-Modify | [MultimediaWiki CDXL](https://wiki.multimedia.cx/index.php/CDXL) |
| [IFF ANIM](https://en.wikipedia.org/wiki/ANIM) | ⚠️ | — | `ANIM`; compression method 5 (Byte Vertical Delta) only, palettised or Hold-And-Modify. The other four methods the specification names are not decoded | [Amiga ANIM IFF](https://wiki.amigaos.net/wiki/ANIM_IFF_CEL_Animations) |
| [BFI](https://wiki.multimedia.cx/index.php/BFI) | ✅ | — | `BFIV`; palettised 8-bit with literal runs, back-references, carried runs and fills | [MultimediaWiki BFI](https://wiki.multimedia.cx/index.php/BFI) |
| [Sierra VMD](https://wiki.multimedia.cx/index.php/VMD) | ⚠️ | — | `VMDV` codec version 2, 8-bit palettised, painted one rectangle at a time. New-palette frames, empty rectangles, LZ rectangles without the preload marker and unknown rendering methods refused | [MultimediaWiki VMD](https://wiki.multimedia.cx/index.php/VMD) |
| [Eidos Escape 124](https://wiki.multimedia.cx/index.php/Escape_124) | ⚠️ | — | ARMovie/RPL codec id 124; 8x8 superblocks, so dimensions not divisible by eight are refused rather than left fringed. Adapted from FFmpeg's LGPL-2.1-or-later decoder | [FFmpeg `escape124.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/escape124.c) |
| [Eidos Escape 130](https://wiki.multimedia.cx/index.php/Escape_130) | ✅ | — | ARMovie/RPL codec id 130; 2x2 blocks, so a picture must be a whole number of them | [MultimediaWiki Escape 130](https://wiki.multimedia.cx/index.php/Escape_130) |

Two of those codecs encode as well as decode — Motion JPEG and planar raw YUV — and both encoders are
reached through `IVideoCodecEncoder<T>` rather than through the registry, which holds decoders only.

Fourteen decoders — Escape 124, LCL MSZH's back-reference parser, LOCO, Canopus Lossless, Matrox
M101, VBLE, MidiVid Archive, MS Screen 1, RemotelyAnywhere, MSCC, MWSC, RSCC, Screenpresso and
WinCAM — are adaptations of FFmpeg's own LGPL-2.1-or-later decoders rather than implementations from
a published description, and each source file names the author it came from. See the licence section
of [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

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
| `VideoFormatRegistry.AllCodecs` | Enumerate registered codecs. |
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

Those rules exist because “find a familiar marker and split there” works on demo files and fails on real media. Detailed validation notes live next to the relevant readers and in [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

## 📚 API reference

<!-- API:BEGIN generated by Hawkynt/RepositoryTemplate/package-readme — edit the XML docs in source, not here -->

Every public and protected member of all 335 types, generated from the built assembly and its XML documentation, is in [REFERENCE.md](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/REFERENCE.md).

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

- Mux support is packet-level remuxing, not codec transcoding. A writer may require container-specific stream description bytes, timing geometry, fragment offsets, or packet-private state when those cannot be reconstructed from coded payload alone.
- H.264/H.265 raw-stream muxers accept Annex B packets only; length-prefixed MP4/QuickTime packet representations are refused rather than silently written as invalid byte streams.
- MP4/QuickTime muxing requires a complete sample entry in `CodecPrivateData` for codecs whose configuration cannot be synthesized safely; missing codec configuration is refused rather than guessed.
- Large RealVideo pictures require preserved slice offsets when they must be split across 16-bit RealMedia packet lengths, and RoQ sound requires its original predictor argument.
- Several advanced codecs intentionally implement well-defined subsets (for example H.264 progressive 8-bit 4:2:0, HEVC Main profile, and VC-1 Simple/Main intra pictures). Every row marked ⚠️ in the codec table names its own subset. Unsupported profiles/features are refused by name rather than silently misdecoded.
- Codec support is more precise than a single green check can express; consult [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md) before relying on a profile/level/feature not named in this README.
- Video correctness depends on real-world packetization as much as codec math. The project therefore validates packet counts, sizes, timestamps, and key-frame flags against external tools where samples are available.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE).
