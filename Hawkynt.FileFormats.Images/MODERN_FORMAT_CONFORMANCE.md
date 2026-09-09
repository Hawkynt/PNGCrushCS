# Modern image codec conformance

This document is the compact interoperability boundary for the modern codecs in `Hawkynt.FileFormats.Images`.
The source-generated registry answers whether an operation exists; this document answers what has actually been
measured against an implementation outside this repository and what is still deliberately refused.

A self-round-trip is not treated as conformance evidence. Lossless paths are compared sample for sample where
an independent decoder is available; lossy paths use the explicitly stated tolerance. Unsupported syntax is
rejected instead of being converted into plausible pixels.

| Format | Read | RawImage write | Current measured scope | Deliberate gaps |
| --- | :---: | :---: | --- | --- |
| WebP | ✅ | ✅ | VP8 keyframes this repository writes are decoded by `dwebp`, sample for sample against this decoder's own reconstruction, at 1, 2, 4 and 8 coefficient token partitions — all four yield identical pixels, which is what RFC 6386 §9.5 partitioning has to preserve. Token emission is deterministic whether or not the partitions are built in parallel. Lossy still-image alpha is preserved in `ALPH`, using headerless VP8L method 1 only when it beats raw method 0, and `dwebp` recovers those alpha planes exactly. Animated `VP8X`/`ANIM`/`ANMF` read/write is verified frame by frame. Multi-pass target-size and target-PSNR rate control search a bounded quality range. VP8L reading is sample-exact against a `cwebp -lossless` corpus and against `dwebp` for all fourteen predictors — see the note below. | VP8 authoring remains keyframe-only; inter-frame reference coding is deliberately left to the video codec path. The VP8L writer emits only the subtract-green transform, so it never produces the predictor, cross-colour or palette forms it can read. |
| JPEG XR | ✅ | ✅ | Managed T.832/JXRLib-derived core; Gray8, RGB24 and RGBA32 are exposed and real `WMPHOTO` codestreams are used. | Component counts other than 1/3/4, interleaved alpha, and some WIC layouts are refused by the current adapter. |
| JPEG 2000 | ✅ | ✅ | Reader parity covers the OpenJPEG corpus described in the package README, including 8/16-bit lossless paths and irreversible 9/7. Writer output is accepted by OpenJPEG, ImageMagick and ffmpeg. | Writer deliberately emits one tile, one layer, reversible 5/3 and LRCP; ROI, POC, packed packet headers and arithmetic bypass are not authored. |
| HEIF / HEIC | ⚠️ | ✅ | Direct HEVC image items decode through the managed H.265 path at 8, 10 and 12 bits for the measured Main-profile intra scope, including monochrome and 4:2:0. The writer emits ordinary HEVC intra PCM coding units accepted by independent decoders. | HEVC 4:2:2, 4:4:4 and depths above 12 remain refused. |
| AVIF | ✅ | ✅ | Managed AV1 still-key-frame reader: 8/10/12 bit; monochrome, 4:2:0, 4:2:2 and 4:4:4; tiles, delta-Q and the measured in-loop filters. The current parity corpus is sample-exact against dav1d. Writer emits a lossless 4:4:4 key frame accepted by `avifdec`, ffmpeg and ImageMagick. | No inter prediction, film grain, super-resolution, scalability, palette blocks, intra block copy or segmentation. Writer has no rate control and does not write alpha. |
| JPEG XL | ⚠️ | ✅ | Still-image reader covers modular and VarDCT, splines, patches, noise, multi-frame composition, 16-bit samples and embedded ICC profiles in the measured libjxl corpus. Lossless modular writer supports 8/16-bit gray, gray+alpha, RGB and RGBA across multiple groups and is checked by `djxl`. | Animations containing lossy VarDCT frames are refused until XYB-domain blending is independently validated. Writer does not author lossy VarDCT. |
| BPG | ⚠️ | — | Container parsing plus the managed HEVC still-image decoder path. `BpgWriter` can re-serialise a `BpgFile` model that already contains valid picture data. | Animation and CMYK are refused. No arbitrary-`RawImage` encoder is registered until a conforming BPG/HEVC authoring path exists. |

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
- [BPG format documentation](https://bellard.org/bpg/) for the HEVC-based BPG container and feature model.

No new runtime dependency is implied by these references; they are specifications, test oracles, and provenance for
behavior. The package codec paths remain managed code.
