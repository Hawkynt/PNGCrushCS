# Modern image-format conformance

This file records the evidence behind the `⚠️` cells in the package README. A capability is only promoted to `✅` after the implementation emits or consumes the normative syntax and has evidence beyond a self-round-trip.

## WebP

Status on this branch: animated read/write implemented.

The writer emits `VP8X`, `ANIM`, and one `ANMF` per frame, including the halved frame offsets, width/height-minus-one fields, 24-bit duration, blend/disposal flags, nested `ALPH`/`VP8`/`VP8L` chunks, loop count, background colour, and extended-WebP metadata ordering. Tests round-trip two independently encoded VP8L frames through the writer and reader and verify frame timing, geometry, alpha, blend, and disposal state.

## MNG

Status on this branch: conforming MNG-VLC writer semantics implemented.

The previous writer encoded the wrong TERM action values, emitted the ten-byte TERM form for actions that require the one-byte form, omitted action 3 (repeat), and wrote incorrect VLC header counts/profile information. The branch fixes those wire values, models repeat delay/action/iteration state, emits the ten-byte form only for repeat, uses the normative MNG-VLC layer/frame/play-time accounting, and writes a truthful VLC simplicity profile that permits transparency. Tests inspect the emitted MHDR and TERM bytes directly.

This remains an MNG-VLC writer; full MNG-LC/full-MNG object buffers, arbitrary framing, loops, JNG, and delta-PNG are separate feature sets and are not disguised as implemented.

## AVIF

Status on `main`: not conforming for real AV1 pixel payloads.

The ISO-BMFF container parser is useful, but the AV1 decoder under `Formats/Avif/Codec` is deliberately not used for real AV1 images. It lacks the normative context-indexed CDF machinery and reads syntax that AV1 arithmetic-codes as equal-probability literals. The reader therefore refuses an AV1-coded payload rather than returning a plausible but incorrect image. The existing writer stores raw raster bytes in `mdat`; it is not an AV1 encoder and is intentionally not registered as the format writer.

Removing the AVIF warning requires a real managed AV1 still-picture decoder and encoder, not additional box plumbing.

## HEIF / HEIC

Status on `main`: container parsing is ahead of the pixel codec.

The repository contains HEVC decoding work used by BPG and video paths, but the current HEIF implementation does not expose a general conforming HEVC decode/encode path suitable for HEIC. There is no managed HEVC encoder registered for HEIF. Promoting HEIF therefore requires extracting/assembling HEVC item bitstreams and completing the required HEVC intra decode/encode profiles rather than substituting a native platform codec.

## JPEG XL

Status on `main`: container/header parsing is useful; pixel coding is not ISO/IEC 18181 interoperable.

`JxlFrameEncoder` and `JxlModularEncoder` currently write a simplified private modular grammar (global predictor selection plus directly bit-packed residuals). That is not the normative JPEG XL modular entropy syntax, and the corresponding decoder agreeing with it is not interoperability evidence. The warning must remain until modular mode is encoded/decoded according to ISO/IEC 18181 and checked against independent JXL files/decoders.

## JPEG 2000

Status on `main`: reader/container work is substantially further along than the EBCOT writer.

Tier-1 coding exists, but the Tier-2 packet writer still serializes code-block inclusion and leading zero-bit-plane information as plain values. JPEG 2000 requires tag-tree coding and packetization by layer/resolution/component/precinct. The existing `TagTree` implementation is not currently wired into those packet headers. A self-round-trip cannot close this gap; output must be accepted by an independent JPEG 2000 decoder.

## JPEG XR

Status on this branch: the synthetic/private pixel codec has been replaced by a managed JXRLib-compatible T.832 implementation.

The old encoder/decoder, transform, quantizer, macroblock, and adaptive-VLC stack has been removed. `Formats/JpegXr/Reference` vendors the pure-managed `SharpAstro.Jxr` reference-code port pinned at commit `7cad99deda0e6c68f68e1c9c64d442c5b85d48a2`; that implementation is itself a faithful managed port of Microsoft's JXRLib and is oracle-tested upstream against `JxrEncApp`, `JxrDecApp`, and Windows WIC. Hawkynt keeps its own T.833 container and `RawImage` integration rather than taking SharpAstro's TIFF/container facade.

The public path now writes real `WMPHOTO` codestreams and standard 16-byte WIC pixel-format GUIDs, and reads those codestreams rather than returning synthetic self-round-trip pixels. Gray8, RGB24, and RGBA32 are exposed through `RawImage`; RGBA uses the standard BCC2/BCC3 planar-alpha codestream. Tests cover pixel-exact lossless Gray8/RGB24/RGBA32 round trips, inspect the emitted T.833 GUID and `WMPHOTO` syntax, and decode the independent JXRLib `red.jxr` fixture through the public reader. That fixture exercises a frequency-order YUV444 colour codestream plus a separate alpha plane, so the interoperability evidence is not a Hawkynt writer/reader loop.

The imported core also contains the JXRLib paths for lossy QP operation, all three overlap modes, spatial/frequency ordering, tiling, YUV444/422/420, and additional integer/float depths. The current `JpegXrFile`/`RawImage` adapter intentionally exposes the package's common 8-bit Gray/RGB/RGBA layouts first; uncommon WIC pixel layouts and interleaved-alpha variants may still be rejected at the container adapter even though the T.832 core is real. Such adapter coverage must not be confused with the former codec-conformance failure.

## Conformance rule

For these formats, `✅` means more than “the project can read what it wrote.” At least one of the following is required for the relevant capability:

- normative bitstream/box assertions for deterministic syntax;
- decoding corpus files produced by an independent implementation;
- independent decoding of files produced by this project;
- pixel comparison against an independent decoder where the format is lossless, or a justified error metric where it is lossy.

Native codec bindings are not used to manufacture green cells: the image-format package remains managed code as required by the repository architecture.
