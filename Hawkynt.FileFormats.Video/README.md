# Hawkynt.FileFormats.Video

[![NuGet](https://img.shields.io/nuget/v/Hawkynt.FileFormats.Video.svg)](https://www.nuget.org/packages/Hawkynt.FileFormats.Video/)
[![CI](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Hawkynt/PNGCrushCS/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/Hawkynt/PNGCrushCS)](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE)
![Target](https://img.shields.io/badge/target-net8.0-blue)

> Pure-C# video containers and codecs with demuxing, decoding, encoding, and muxing kept as separate contracts so packet-level remuxing never has to become a decode/re-encode by accident.

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

The tables below are the package-level overview. The detailed codec-by-codec implementation state and known subset restrictions live in [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

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

### Codec highlights

| Codec | Decode | Encode | Implemented scope / note | Reference |
| --- | :---: | :---: | --- | --- |
| [Motion JPEG](https://en.wikipedia.org/wiki/Motion_JPEG) | ✅ | — | JPEG frames in supported containers/streams | [ITU-T T.81](https://www.itu.int/rec/T-REC-T.81) |
| [MPEG-1 Video](https://en.wikipedia.org/wiki/MPEG-1) | ✅ | — | picture decode | [ISO/IEC 11172-2 overview](https://mpeg.chiariglione.org/standards/mpeg-1/video) |
| [MPEG-2 Video](https://en.wikipedia.org/wiki/H.262/MPEG-2_Part_2) | ✅ | — | picture decode | [ITU-T H.262](https://www.itu.int/rec/T-REC-H.262) |
| [H.264 / AVC](https://en.wikipedia.org/wiki/Advanced_Video_Coding) | ⚠️ | — | Baseline I/P subset | [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264) |
| [H.265 / HEVC](https://en.wikipedia.org/wiki/High_Efficiency_Video_Coding) | ⚠️ | — | Main-profile intra subset | [ITU-T H.265](https://www.itu.int/rec/T-REC-H.265) |
| [VP8](https://en.wikipedia.org/wiki/VP8) | ✅ | — | supported VP8 decode path | [RFC 6386](https://www.rfc-editor.org/rfc/rfc6386) |
| [VP9](https://en.wikipedia.org/wiki/VP9) | ⚠️ | — | profile 0 | [WebM VP9](https://www.webmproject.org/vp9/) |
| [Theora](https://en.wikipedia.org/wiki/Theora) | ✅ | — | Xiph.Org Theora I | [Theora specification](https://www.theora.org/doc/Theora.pdf) |
| [FFV1](https://en.wikipedia.org/wiki/FFV1) | ✅ | — | lossless | [RFC 9043](https://www.rfc-editor.org/rfc/rfc9043) |
| [Apple ProRes](https://en.wikipedia.org/wiki/Apple_ProRes) | ✅ | — | common ProRes profiles | [Apple ProRes white paper](https://www.apple.com/final-cut-pro/docs/Apple_ProRes_White_Paper.pdf) |
| [Avid DNxHD / DNxHR](https://en.wikipedia.org/wiki/DNxHD_codec) | ✅ | — | SMPTE VC-3 family | [SMPTE VC-3 overview](https://ieeexplore.ieee.org/document/7290708) |
| [GoPro CineForm](https://en.wikipedia.org/wiki/CineForm) | ✅ | — | SMPTE VC-5 family | [LOC CineForm overview](https://www.loc.gov/preservation/digital/formats/fdd/fdd000458.shtml) |
| [Microsoft Video 1](https://wiki.multimedia.cx/index.php/Microsoft_Video_1) | ✅ | — | CRAM/MSVC/WHAM | [MultimediaWiki](https://wiki.multimedia.cx/index.php/Microsoft_Video_1) |
| [Cinepak](https://en.wikipedia.org/wiki/Cinepak) | ✅ | — | QuickTime/AVI Cinepak | [MultimediaWiki Cinepak](https://wiki.multimedia.cx/index.php/Cinepak) |
| [HuffYUV](https://en.wikipedia.org/wiki/Huffyuv) | ✅ | — | HuffYUV / FFVHUFF variants | [MultimediaWiki HuffYUV](https://wiki.multimedia.cx/index.php/HuffYUV) |
| [Ut Video](https://en.wikipedia.org/wiki/Ut_Video) | ✅ | — | common RGB/YUV variants | [Ut Video](https://github.com/umezawatakeshi/utvideo) |
| [MagicYUV](https://en.wikipedia.org/wiki/MagicYUV) | ✅ | — | common RGB/YUV variants | [MagicYUV](https://www.magicyuv.com/) |
| [TechSmith Screen Capture](https://wiki.multimedia.cx/index.php/TechSmith_Screen_Capture_Codec) | ✅ | — | TSCC | [MultimediaWiki TSCC](https://wiki.multimedia.cx/index.php/TechSmith_Screen_Capture_Codec) |
| [CamStudio](https://en.wikipedia.org/wiki/CamStudio) | ✅ | — | CSCD | [MultimediaWiki CamStudio](https://wiki.multimedia.cx/index.php/CamStudio) |
| [Hap](https://en.wikipedia.org/wiki/Hap_(video_codec)) | ⚠️ | — | supported Hap variants; unsupported variants are refused explicitly | [Vidvox Hap](https://hap.video/) |

For the long tail (QuickTime Animation, RPZA, SMC, H.261/H.263, RealVideo 1, VC-1/WMV, FLIC, ZMBV, screen codecs, uncompressed packed YUV/RGB families, game codecs, QPEG, ASUS, Creative YUV, MJPEG-B, AVRn, Escape 124, Escape 130, MSZH, LOCO, Canopus Lossless, Matrox M101, VBLE, MidiVid Archive, MSS1, RASC, MSCC, MWSC, RSCC, Screenpresso, WCMV, and others), see [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

Fourteen of those — Escape 124, MSZH, LOCO, Canopus Lossless, Matrox M101, VBLE, MidiVid Archive, MSS1, RASC, MSCC, MWSC, RSCC, Screenpresso and WCMV — are adaptations of FFmpeg's own LGPL-2.1-or-later decoders rather than implementations from a published description, and each source file names the author it came from. See the licence section of [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md).

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
- Several advanced codecs intentionally implement well-defined subsets (for example H.264 Baseline and HEVC Main intra paths). Unsupported profiles/features should be refused rather than silently misdecoded.
- Codec support is more precise than a single green check can express; consult [`codec-coverage.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-coverage.md) before relying on a profile/level/feature not named in this README.
- Video correctness depends on real-world packetization as much as codec math. The project therefore validates packet counts, sizes, timestamps, and key-frame flags against external tools where samples are available.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](https://github.com/Hawkynt/PNGCrushCS/blob/main/LICENSE).
