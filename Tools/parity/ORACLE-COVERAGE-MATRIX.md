# Image and video oracle coverage matrix

This file answers a narrower question than `ORACLES.md`: **do we have enough independent external
implementations to implement a format without having to trust our own reader/writer pair?**

The answer is yes for most standardized and commercially important image/video families, but not for
every historical or proprietary format. `reference-ready` below means a format family has a
normative/reference source plus at least one useful independent implementation, or multiple genuinely
independent native implementations. `platform-ready` means a vendor/OS implementation gives strong
additional evidence but the harness is necessarily platform-specific. `oracle-limited` means the
public ecosystem does not currently provide two demonstrably independent engines.

Nothing in this matrix is a `[VerifiedBy]` claim. A claim is earned only after the named engine has
actually consumed bytes from our writer or produced bytes compared against our reader.

## Image families

| Family | Primary / reference evidence | Independent cross-check | Status | Notes |
| --- | --- | --- | --- | --- |
| PNG | libpng + PNG specification/test material | libspng, Wuffs, Windows WIC, Apple ImageIO | reference-ready | libpng describes itself as the official PNG reference library; libspng is deliberately independent of libpng |
| JPEG/JFIF | libjpeg-turbo/IJG + JPEG specification/vectors | Wuffs, WIC, ImageIO | reference-ready | Compare decoded pixels with declared color-conversion/tolerance rules rather than file bytes |
| GIF | giflib + GIF89a | Wuffs, WIC, ImageIO | reference-ready | Include interlace, animation, transparency and disposal methods |
| TIFF | libtiff + TIFF/BigTIFF specifications | WIC, ImageIO, specialist readers | reference-ready | Exercise endian order, strips, tiles, planar/chunky data and compression variants separately |
| WebP | libwebp | Wuffs; WIC WebP extension where installed | reference-ready | Lossless, lossy, alpha and animation are separate corpus dimensions |
| QOI | QOI reference implementation | Wuffs | reference-ready | Small enough to exhaust boundary/channel cases aggressively |
| OpenEXR | OpenEXR reference implementation | TinyEXR | reference-ready | TinyEXR's unsupported compression/storage modes do not count as coverage of those modes |
| JPEG XR / HD Photo | ITU-T T.832/T.835 + Microsoft's jxrlib reference implementation | native Windows WIC JPEG XR codec | reference-ready / platform-ready | `JXREncApp`/`JXRDecApp` are useful corpus producers/consumers |
| DDS / BC1-BC7 | Microsoft DirectXTex | AMD Compressonator; native WIC DDS for supported subsets | reference-ready / platform-ready | DirectXTex has an independent DDS reader/writer and BC1-BC7 implementations; Compressonator covers BC1-BC7 plus ETC/ASTC |
| ASTC | Arm `astcenc` + Khronos data-format specification | AMD Compressonator | reference-ready | KTX container validation remains a separate concern |
| KTX/KTX2 | Khronos KTX-Software/specification | independent texture decoders after payload extraction | reference-ready | Validate container and block codec independently |
| JPEG 2000 | OpenJPEG + ISO/ITU specification/vectors | second independent application/OS decoder where available | reference-ready with per-profile checks | Do not treat two OpenJPEG frontends as two engines |
| JPEG XL | libjxl + JPEG XL specification/conformance material | independent platform/application decoder when actually available | reference-heavy | Reference/native evidence is strong; second fully independent decoder availability is thinner than PNG/JPEG |
| AVIF/HEIF | libavif/libaom and libheif | OS codecs (ImageIO/WIC extension) when enumerated and actually used | reference-ready / platform-ready | Separate ISOBMFF item/property validation from AV1/HEVC payload validation |
| camera RAW | camera samples + LibRaw | vendor software / OS RAW codecs for specific camera models | format-specific | RAW is a family of vendor formats, not one codec; verify per camera/container generation |
| DICOM | DICOM standard + DCMTK | ITK/Convert3D or another DICOM implementation where pixel transfer syntax overlaps | reference-ready by transfer syntax | Container metadata and compressed pixel payloads need separate checks |
| NIfTI/NRRD/MetaImage/MRC | published format specs + ITK/Convert3D | GDAL/other scientific tools where overlap exists | reference-ready by format | Preserve dimensions, spacing, orientation, endianness and scalar type, not just a 2-D preview |
| retro/historical rasters | original format docs where available | RECOIL, Deark, XnView, IrfanView, Netpbm | mixed | Existing `coverage-gap.md` already records the residual cases for which no credible public decoder/spec was found; those stay oracle-limited |

### Additional image platform oracles

**Windows Imaging Component (WIC).** Microsoft's current documentation lists native read/write codecs
for BMP, GIF, JPEG, JPEG XR, PNG, TIFF, Windows Media Photo/HD Photo and DDS, with ICO read support.
DNG, HEIF and WebP can additionally be supplied by Microsoft extension codecs. A WIC harness must
record the actual codec CLSID/vendor/version so that an arbitrary third-party WIC plug-in is not
mistaken for Microsoft's independent implementation.

- https://learn.microsoft.com/windows/win32/wic/-wic-about-windows-imaging-codec
- https://learn.microsoft.com/windows/win32/wic/native-wic-codecs

**Apple ImageIO.** ImageIO exposes the image-source and image-destination type lists at runtime through
`CGImageSourceCopyTypeIdentifiers()` and `CGImageDestinationCopyTypeIdentifiers()`. The harness should
enumerate those lists on the executing OS and record the OS version rather than hard-code a format
list that can change between releases.

- https://developer.apple.com/documentation/imageio
- https://developer.apple.com/documentation/imageio/cgimagesourcecopytypeidentifiers()
- https://developer.apple.com/documentation/imageio/cgimagedestinationcopytypeidentifiers()

**DirectXTex and Compressonator.** These are especially useful for texture formats because a generic
image viewer frequently delegates DDS/BCn decoding to the OS or does not expose block-level behavior.
DirectXTex is Microsoft's MIT-licensed DDS/texture implementation; AMD's Compressonator independently
supports BC1-BC7, ETC1/2, ASTC, ATC and ATI1N/ATI2N.

- https://github.com/microsoft/DirectXTex
- https://github.com/GPUOpen-Tools/compressonator

**JPEG XR reference software.** Microsoft's released `jxrlib`/DPK is the JPEG XR reference
implementation associated with ITU-T T.835; archived mirrors retain `JXREncApp` and `JXRDecApp`. Treat
it as reference evidence and WIC as the independent Windows implementation, not as two votes from the
same binary.

- https://www.itu.int/rec/T-REC-T.832
- https://www.itu.int/rec/T-REC-T.835
- https://github.com/4creators/jxrlib

## Video codec families

| Family | Primary / producer evidence | Independent consumer evidence | Status | Notes |
| --- | --- | --- | --- | --- |
| AV1 | libaom, SVT-AV1, rav1e + AOM vectors | dav1d, libaom | reference-ready | Exercise profiles, bit depths, chroma modes, tiles, film grain and inter prediction separately |
| VP8/VP9 | libvpx + WebM test material | a separately verified non-libvpx decoder where practical | reference-heavy | Keep IVF/WebM parsing separate from codec decoding |
| H.264/AVC | JM reference software, x264, OpenH264 | JM, OpenH264, Windows Media Foundation, Apple VideoToolbox | reference-ready / platform-ready | Published H.264.1/JVT conformance streams are the fixed baseline |
| HEVC/H.265 | HM reference software, x265, Kvazaar | HM, libde265, Apple VideoToolbox | reference-ready / platform-ready | Published H.265.1 conformance streams are the fixed baseline |
| VVC/H.266 | VTM, VVenC | VTM, VVdeC | reference-ready | VTM is the reference model; VVenC/VVdeC add implementation diversity |
| MPEG-4 Part 2 | Xvid, reference/spec material | Xvid, Media Foundation, FFmpeg | reference-ready / platform-ready | Test GMC/qpel/profile boundaries explicitly |
| Microsoft MPEG-4 / WMV / WMV Screen | Microsoft Media Foundation/native Windows codecs | FFmpeg where supported | platform-ready | This is one of the strongest ways to escape an FFmpeg-only oracle for Microsoft-origin codecs |
| DV | specification/sample corpus | Windows Media Foundation, Apple platform codecs, FFmpeg | platform-ready | Verify PAL/NTSC and chroma/audio framing variants separately |
| Apple ProRes | Apple codec definitions / platform implementation | Apple VideoToolbox/AVFoundation plus FFmpeg | platform-ready | Apple documents ProRes codec types and direct VideoToolbox compression/decompression; use macOS as the vendor oracle |
| Avid DNxHD/DNxHR / VC-3 | SMPTE ST 2019-1 + Avid DNx SDK | FFmpeg plus Avid SDK decode | vendor-reference-ready | Avid states its current SDK reads/writes its VC-3 implementation; evaluation use is licensed and must remain external to the project |
| GoPro CineForm | GoPro CineForm SDK | FFmpeg | vendor-reference-ready | Released SDK contains encoder and decoder paths |
| Hap | Vidvox Hap specification/reference source | Vidvox test packs from FFmpeg, TouchDesigner, AVF Batch Converter, QuickTime and DirectShow producers | vendor-reference-ready | Excellent differential corpus because the upstream project publishes samples from multiple encoders |
| MagicYUV | MagicYUV SDK/native codec | FFmpeg | vendor-reference-ready | Proprietary SDK/native codec is useful only as an executable oracle; do not copy SDK implementation code |
| Theora | libtheora | FFmpeg / independently verified implementation | reference-ready | Xiph's implementation is the native baseline |
| VC-2/Dirac | BBC VC-2 conformance suite | reference decoder + bitstream validator | reference-ready | The suite explicitly provides encoder/decoder conformance procedures |
| legacy QuickTime/VfW codecs | original/vendor codec when runnable | FFmpeg plus a demonstrably independent decoder | mixed | Apple Video, Cinepak, Indeo, MS Video 1 and similar codecs need per-codec lineage checks |
| game/proprietary codecs | original game/media corpus + format notes | FFmpeg or another surviving decoder | oracle-limited for many families | ScummVM is useful only after checking lineage per codec; its Smacker decoder explicitly derives from FFmpeg and therefore is not a second Smacker engine |

### Additional video platform/vendor oracles

**Windows Media Foundation.** Microsoft documents native file sources/sinks for 3GP, ASF and MPEG-4,
plus an AVI source, and native video codecs across several Microsoft and standards-based families.
Use the native Microsoft transform identity in the harness; Media Foundation is extensible, so an
installed third-party MFT must not be counted as Microsoft evidence.

- https://learn.microsoft.com/windows/win32/medfound/supported-media-formats-in-media-foundation
- https://learn.microsoft.com/windows/apps/develop/media-authoring-processing/supported-codecs

**Apple VideoToolbox.** Apple exposes direct compression/decompression sessions and CoreMedia codec
identifiers for ProRes variants, HEVC, VP9, DV, Cinepak and others. Actual encoder/decoder availability
is hardware/OS-dependent, so the harness must attempt session creation and record OS/hardware rather
than infer support from the existence of a FourCC constant. Apple also publishes a ProRes decoding
sample using `VTDecompressionSession`.

- https://developer.apple.com/documentation/videotoolbox
- https://developer.apple.com/documentation/coremedia/cmvideocodectype
- https://developer.apple.com/videos/play/wwdc2020/10090/

**Avid DNx SDK.** Avid's current developer page describes its DNx SDK as a reader/writer for the
current SMPTE ST 2019-1:2026 VC-3 definitions and provides a downloadable evaluation toolkit. The SDK
license is non-commercial/non-redistributable for the public evaluation package; therefore execute it
only as an external behavioural oracle and do not redistribute, link, copy or translate its code.

- https://developer.avid.com/

**Vidvox Hap.** The upstream Hap project contains the format specification and reference encode/decode
source under BSD-2-Clause, plus test packs made by several independent applications. That gives Hap a
substantially better oracle story than an FFmpeg-only round trip.

- https://github.com/Vidvox/hap

**MagicYUV.** The vendor offers a cross-platform SDK and native codecs. It is proprietary, so it is an
external oracle only; the independently implemented C# codec must remain derived from public behavior,
format facts and tests rather than SDK source.

- https://www.magicyuv.com/

## Container families

| Container | Independent parsers / muxers | Status |
| --- | --- | --- |
| ISO-BMFF / MP4 / MOV / 3GP | GPAC/MP4Box, Bento4, Shaka Packager, Windows Media Foundation; Apple platform stack | reference-ready / platform-ready |
| Matroska / WebM | MKVToolNix, libwebm, Shaka Packager | reference-ready |
| Ogg | libogg/oggz | reference-ready |
| MPEG-2 TS | Shaka Packager, GPAC, FFmpeg | reference-ready |
| AVI | Windows Media Foundation plus FFmpeg; RIFF structure checks | platform-ready |
| ASF | Windows Media Foundation plus FFmpeg | platform-ready |
| FLV | FFmpeg plus independent structural parser/test corpus still desirable | limited-secondary |
| RealMedia | FFmpeg plus independent historical parser/original files still desirable | oracle-limited |
| FLIC/RoQ/MVE/CIN/VQA/Smacker/EA/BFI/CDXL/ANIM/VMD/STR/RPL | format-specific tools/original applications where obtainable | mixed/oracle-limited |

A container parser accepting a file proves container structure only. A codec decoder accepting the
extracted packet proves the codec only. One must not be used as evidence for the other.

## Practical gate for future implementations

Before changing an image/video row to fully supported:

1. identify the normative specification/reference software if one exists;
2. identify at least one implementation with genuinely independent lineage;
3. generate foreign input covering every supported profile/mode/bit depth/chroma/layout;
4. compare our decode against a reference decode at pixel/sample level;
5. feed our writer/encoder output to the reference and independent decoders;
6. validate the containing file separately with an independent container parser;
7. add malformed/truncated/overflow and published negative vectors;
8. record tool version, backend identity, OS/hardware where platform codecs are involved;
9. add `[VerifiedBy]` only for engines that were actually executed successfully;
10. label the format `oracle-limited` when step 2 is impossible instead of substituting another
    frontend for the same engine.

## Residual whole-domain limit

No finite collection of current tools can make every historical image/video implementation
independently provable. The repository's existing `coverage-gap.md` records image formats whose only
known readers were unavailable proprietary plug-ins or whose public references were unusable; similar
conditions exist among old game-video codecs and containers. Those cases can still be implemented
carefully from specifications, original files, original applications and one surviving decoder, but
they cannot honestly receive the same confidence label as PNG, AVC, HEVC or VVC.

That is the boundary to preserve: **whole-domain implementation is achievable as a tracked goal;
whole-domain two-oracle verification is not currently achievable for formats whose independent
implementations no longer exist.**
