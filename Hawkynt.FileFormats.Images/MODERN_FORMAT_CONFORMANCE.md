# Modern image codec conformance

This document is the compact interoperability boundary for the modern codecs in `Hawkynt.FileFormats.Images`.
The source-generated registry answers whether an operation exists; this document answers what has actually been
measured against an implementation outside this repository and what is still deliberately refused.

A self-round-trip is not treated as conformance evidence. Lossless paths are compared sample for sample where
an independent decoder is available; lossy paths use the explicitly stated tolerance. Unsupported syntax is
rejected instead of being converted into plausible pixels.

| Format | Read | RawImage write | Current measured scope | Deliberate gaps |
| --- | :---: | :---: | --- | --- |
| WebP | ✅ | ✅ | VP8 keyframes this repository writes are decoded by `dwebp`, sample for sample against this decoder's own reconstruction, at 1, 2, 4 and 8 coefficient token partitions — all four yield identical pixels, which is what RFC 6386 §9.5 partitioning has to preserve. That holds over 17 geometries from 1x1 to 640x480, on and off the macroblock grid, and ffmpeg reads every one of them as well. Token emission is deterministic whether or not the partitions are built in parallel. What the lossless writer produces is decoded sample for sample by `dwebp` and by ffmpeg too, over 833 pictures across the same range — gradients, noise, flat fields, index ramps, single rows, single columns, with and without alpha — and over 1,700 further randomised ones. It takes a corpus that size: the writer used to emit a code-length code holding a single symbol whenever an alphabet came out perfectly even, as 256 literals eight bits deep do, and a prefix code over one symbol is resolved without reading a bit where the writer had spent one, so every code length after it landed a bit out of place. It struck by content rather than by size, and every fixture until now was small enough that no alphabet filled that evenly. Lossy still-image alpha is preserved in `ALPH`, using headerless VP8L method 1 only when it beats raw method 0, and `dwebp` recovers those alpha planes exactly. Animated `VP8X`/`ANIM`/`ANMF` read/write is verified frame by frame. Multi-pass target-size and target-PSNR rate control search a bounded quality range. VP8L reading is sample-exact against a `cwebp -lossless` corpus and against `dwebp` for all fourteen predictors — see the note below. | VP8 authoring remains keyframe-only; inter-frame reference coding is deliberately left to the video codec path. The VP8L writer emits only the subtract-green transform, so it never produces the predictor, cross-colour or palette forms it can read. |
| JPEG XR | ✅ | ✅ | Managed T.832/JXRLib-derived core; Gray8, RGB24 and RGBA32 are exposed and real `WMPHOTO` codestreams are used. | Component counts other than 1/3/4, interleaved alpha, and some WIC layouts are refused by the current adapter. |
| JPEG 2000 | ✅ | ✅ | Reader parity covers the OpenJPEG corpus described in the package README, including 8/16-bit lossless paths and irreversible 9/7. Writer output is accepted by OpenJPEG, ImageMagick and ffmpeg. | Writer deliberately emits one tile, one layer, reversible 5/3 and LRCP; ROI, POC, packed packet headers and arithmetic bypass are not authored. |
| HEIF / HEIC | ⚠️ | ✅ | Direct HEVC image items decode through the managed H.265 path at 8, 10 and 12 bits in every chroma format the standard defines — monochrome, 4:2:0, 4:2:2 and 4:4:4. Measured on the coded planes, never on RGB: 158 intra streams written by x265 4.3 and libheif 1.23.3, decoded sample for sample by ffmpeg 9.0.1 and by `dec265` from libde265 1.1.2; 156 agree with both, and the two that do not are the two oracles disagreeing with each other. The writer emits ordinary HEVC intra PCM coding units accepted by independent decoders. | Samples deeper than twelve bits remain refused, for want of an oracle rather than of syntax: x265 builds eight, ten and twelve bits and nothing beyond, and libheif encodes HEVC through x265, so nothing to hand can write a stream to check such a decode against. The range- and screen-content-extension coding tools and separately coded colour planes stay refused by name. |
| AVIF | ✅ | ✅ | Managed AV1 still-key-frame reader: 8/10/12 bit; monochrome, 4:2:0, 4:2:2 and 4:4:4; tiles, delta-Q and the measured in-loop filters. The current parity corpus is sample-exact against dav1d. Writer emits a lossless 4:4:4 key frame accepted by `avifdec`, ffmpeg and ImageMagick. | No inter prediction, film grain, super-resolution, scalability, palette blocks, intra block copy or segmentation. Writer has no rate control and does not write alpha. |
| JPEG XL | ⚠️ | ✅ | Still-image reader covers modular and VarDCT, splines, patches, noise, multi-frame composition, 16-bit samples and embedded ICC profiles in the measured libjxl corpus. Lossless modular writer supports 8/16-bit gray, gray+alpha, RGB and RGBA across multiple groups and is checked by `djxl`. | Animations containing lossy VarDCT frames are refused until XYB-domain blending is independently validated. Writer does not author lossy VarDCT. |
| BPG | ⚠️ | ✅ | The writer emits 8-bit 4:4:4 in BPG's RGB colour space — G, B and R in the three planes, no colour matrix — coded entirely from HEVC intra PCM coding units. 39 pictures written and decoded by Fabrice Bellard's own `bpgdec` 0.9.8, sample for sample exact on all 39: sizes from 1×1 to 320×240, on and off the 32-sample tree-block grid (1×64, 64×1, 31×31, 33×17, 129×3, 200×137), plasma and pure noise, all-black and all-white, and from Rgb24, Rgba32, Bgra32 and Gray8 sources. Reading covers container parsing plus the managed HEVC still-image decoder path. | The writer authors that one profile and refuses the rest by name: deeper bit depths, 4:2:0 and 4:2:2 chroma, the YCbCr and YCgCo colour spaces, limited range, alpha, CMYK, animation, and the Exif/ICC/XMP/thumbnail extension tags. `pixel_format` 0 — a single grey plane — is refused because of the reference decoder rather than this encoder: `libbpg`'s `hls_pcm_sample` writes a coding unit's chroma blocks unconditionally and so faults on a monochrome frame, which makes a monochrome PCM picture unverifiable. A grey picture is written as three equal planes instead. Reading refuses animation and CMYK; the general (transform-coded) BPG decode path does not yet reconstruct the parameter sets a real BPG file omits, so it reads pictures this package wrote and not ones `bpgenc` wrote. |

## Reading what other encoders write

Everything this package writes went through subtract-green and nothing else, so for a long time no test
touched the parts of the VP8L reader that only other encoders reach. Three faults lived there, and all three
are fixed:

- **Predictors.** Five of the fourteen were wired to the wrong neighbours — 5 must average left, top and
  top-right; 6 is left with top-left; 7 is left with top; 8 is top-left with top; 9 is top with top-right —
  predictor 11 broke ties towards the left neighbour where the format breaks them towards the top one, and the
  top-right neighbour in the last column is the first pixel of the row being reconstructed, not the pixel
  above it.
- **Position-coded distances.** The tail of the 120-entry table that turns a distance code into a nearby
  pixel had been extrapolated from the pattern the first three quarters appear to follow. The real table
  stops following it, so codes from 97 up named the wrong pixel. The table is now stored packed and unpacked
  by the format's own rule, which is what makes the extrapolation impossible to repeat.
- **Long Huffman codes.** A secondary-table pointer was indistinguishable from a symbol, so any code longer
  than eight bits decoded as garbage.

Measured on a corpus of 102 `cwebp -lossless` files spanning ramps, curvature, hard edges, noise, palettes,
single-row, single-column and one-pixel pictures at `-m 0`, `-m 6 -q 100` and `-z 9`: 20 decoded inexactly
before, none do now. All fourteen predictors and the first-row, first-column and last-column rules are checked
against `dwebp` on 98 purpose-built streams — cwebp never selects predictors 6 or 8, so those streams are
written directly rather than encoded — of which 34 disagreed with `dwebp` before and none do now. The `ALPH`
chunk of a `cwebp` lossy-plus-alpha picture went from 172 of 2048 alpha samples wrong, worst error 224, to
exact.

## Evidence

The detailed corpus counts, tolerances and edge cases remain in [`README.md`](README.md#conformance-evidence-for-the-modern-codecs),
with executable parity tooling under [`../Tools/parity`](../Tools/parity) and codec-specific NUnit fixtures under
[`../Tests/Hawkynt.FileFormats.Images.Tests`](../Tests/Hawkynt.FileFormats.Images.Tests).

Primary specifications and reference implementations used by these paths include:

- [WebP container specification](https://developers.google.com/speed/webp/docs/riff_container) and [RFC 6386](https://www.rfc-editor.org/rfc/rfc6386) for VP8.
- [OpenJPEG](https://www.openjpeg.org/) and the JPEG 2000 family specifications for JPEG 2000 interoperability.
- [ISO/IEC 23008-12 / HEIF background](https://www.iso.org/standard/83650.html), [libheif](https://github.com/strukturag/libheif), and independent HEVC decoders for HEIF/HEIC.
- [AV1 Bitstream & Decoding Process Specification](https://aomediacodec.github.io/av1-spec/), [libaom](https://aomedia.googlesource.com/aom/) and [dav1d](https://code.videolan.org/videolan/dav1d) for AVIF.
- [JPEG XL specification resources](https://jpeg.org/jpegxl/) and [libjxl](https://github.com/libjxl/libjxl) for JPEG XL.
- [BPG format documentation](https://bellard.org/bpg/) for the HEVC-based BPG container and feature model; `doc/bpg_spec.txt` from the `libbpg` distribution is the normative syntax, and its `bpgdec` is the oracle the writer is measured against.

No new runtime dependency is implied by these references; they are specifications, test oracles, and provenance for
behavior. The package codec paths remain managed code.
