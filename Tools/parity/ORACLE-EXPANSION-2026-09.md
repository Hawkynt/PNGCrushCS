# Additional image/video oracle expansion — 2026-09

This note extends `ORACLE-COVERAGE-MATRIX.md` with additional reference/native engines found while
asking whether the repository can safely implement the complete image and video domains.

The conclusion is deliberately asymmetric:

- mainstream standardized image/video families now have enough independent evidence sources for a
  strong implementation workflow;
- a substantial set of historical/proprietary formats still cannot honestly reach a two-independent-
  engine standard because only one surviving public decoder (often FFmpeg) or one vendor binary is
  available;
- those formats are still implementable from specifications, corpora, original applications and one
  surviving decoder, but must remain explicitly `oracle-limited`.

Merely listing a tool here is not a `[VerifiedBy]` claim. An implementation earns such a claim only
when the exact external engine is actually executed against the bytes under test.

## Additional image engines worth wiring into the harness

| Oracle | Best coverage | Independence value | License / use | Upstream |
| --- | --- | --- | --- | --- |
| CharLS | JPEG-LS / ITU-T T.87 | Native implementation independent of FFmpeg/ImageMagick | BSD-3-Clause; safe as external oracle | https://github.com/team-charls/charls |
| JasPer | JPEG 2000 Part 1 | Published JPEG-2000 Part-5 reference implementation lineage | JasPer license; external execution only is sufficient | https://github.com/jasper-software/jasper |
| OpenJPH | HTJ2K / JPEG 2000 Part 15 | Independent modern HTJ2K implementation | BSD-2-Clause | https://github.com/aous72/OpenJPH |
| OpenHTJ2K | JPEG 2000 Part 1 + HTJ2K | Additional independent implementation with conformance testing | project license; external oracle | https://github.com/osamu620/OpenHTJ2K |
| Microsoft jxrlib | JPEG XR / HD Photo | Reference implementation associated with ITU-T T.835 | BSD-2-Clause mirror of Microsoft release | https://github.com/4creators/jxrlib |
| DirectXTex | DDS, BC1-BC7, TGA, HDR, WIC-backed formats | Microsoft-native DDS/BCn implementation, especially valuable for texture formats | MIT | https://github.com/microsoft/DirectXTex |
| AMD Compressonator | BC1-BC7, ETC1/2, ASTC and related GPU textures | Independent texture codec implementation vs DirectXTex/astcenc | external oracle | https://github.com/GPUOpen-Tools/compressonator |
| Basis Universal | Basis, KTX2 ETC1S/UASTC and texture transcodes | Reference/native implementation for Basis-family payloads | Apache-2.0 | https://github.com/BinomialLLC/basis_universal |
| CFITSIO | FITS | Long-lived NASA/HEASARC read/write implementation | external oracle | https://heasarc.gsfc.nasa.gov/docs/software/fitsio/fitsio.html |
| librsvg | static SVG | Independent renderer compared with ImageMagick/browser stacks | LGPL-2.1-or-later | https://gitlab.gnome.org/GNOME/librsvg |
| Poppler | PDF rasterization/inspection | Independent PDF engine compared with Ghostscript | GPL; execute only, do not link/copy | https://poppler.freedesktop.org/ |
| OpenSlide | whole-slide pathology formats | Independent vendor-neutral reader for SVS/NDPI/MRXS/SCN/CZI/etc. | LGPL-2.1 | https://openslide.org/ |

### Image-specific confidence upgrades

The following families should now be treated as especially strong targets for full reader/writer
work because they have reference/native software plus at least one useful independent cross-check:

- JPEG-LS: specification + CharLS + DICOM stacks where JPEG-LS transfer syntax is supported.
- JPEG 2000 Part 1: OpenJPEG + JasPer; use OpenJPH/OpenHTJ2K for HTJ2K rather than assuming Part 1
  coverage proves Part 15.
- JPEG XR: ITU-T T.832/T.835 + jxrlib + native Windows WIC.
- DDS/BCn: DirectXTex + AMD Compressonator; KTX/ASTC payloads can additionally use KTX-Software and
  Arm `astcenc`.
- FITS: CFITSIO + a second scientific reader where the exercised HDU/pixel type overlaps.
- PDF/SVG: Ghostscript + Poppler for PDF, and librsvg plus a browser/platform renderer for SVG.

## Additional video engines and official/reference software

| Oracle | Best coverage | Independence value | License / use | Upstream |
| --- | --- | --- | --- | --- |
| MPEG-1 reference software | ISO/IEC 11172 video | Normative/reference software route | external/reference software | https://mpeg.chiariglione.org/technologies/reference-software/mpeg-1-reference-software.html |
| MPEG-2 reference software | ISO/IEC 13818-2 / H.262 | C software simulation of encoder/decoder | external/reference software | https://mpeg.chiariglione.org/technologies/reference-software/mpeg-2-reference-software.html |
| MPEG-4 reference software | ISO/IEC 14496-2 and related MPEG-4 media | Normative/reference decoder lineage | external/reference software | https://mpeg.chiariglione.org/standards/mpeg-4/reference-software.html |
| ITU H.263 TMN / Appendix III | H.263 | Standard-published encoder/decoder example model | reference material/software | https://www.itu.int/rec/T-REC-H.263/ |
| libmpeg2 | MPEG-1/2 video | Independent production decoder vs FFmpeg/reference model | GPL; execute only | https://www.videolan.org/developers/libmpeg2.html |
| libgav1 | AV1 | Third independent decoder in addition to libaom/dav1d | Apache-2.0 | https://chromium.googlesource.com/codecs/libgav1/ |
| Vidvox Hap reference | Hap/Hap Alpha/Hap Q/Hap R/HDR families | Vendor specification plus reference encode/decode source and independent sample packs | BSD-2-Clause | https://github.com/Vidvox/hap |
| RAD Video Tools | Smacker and Bink | Original vendor compressor/player; strongest possible behavioral oracle for those proprietary codecs | proprietary binary oracle only | https://www.radgametools.com/ |
| ScummVM | several game/video formats | Useful only after per-codec lineage review; some decoders are independent, some explicitly derive from FFmpeg | GPL-3.0; execute only | https://github.com/scummvm/scummvm |
| xavs2 / davs2 | AVS2 / IEEE 1857.4 | Native encoder/decoder pair plus published conformance streams | GPL-2.0; external oracle | https://github.com/pkuvcl/xavs2 / https://github.com/pkuvcl/davs2 |
| uavs3e / uavs3d | AVS3 | Native encoder/decoder implementations from the AVS3 development community | BSD variants; external oracle | https://github.com/uavs3/uavs3e / https://github.com/uavs3/uavs3d |
| Thor reference implementation | experimental Thor codec | Native reference implementation for the retired NETVC candidate | BSD-2-Clause | https://github.com/cisco/thor |
| Daala reference implementation | Daala | Xiph/Mozilla reference implementation | BSD-2-Clause | https://github.com/xiph/daala |

### Important lineage caveats

ScummVM must not be counted blindly as an independent second decoder. For example, its Smacker
implementation explicitly states that it is based on the FFmpeg Smacker decoder, and its Bink decoder
also documents substantial FFmpeg lineage. Those are useful compatibility oracles but not independent
votes against FFmpeg. For Smacker/Bink, the original RAD tools are the meaningful second engine.

Likewise, VLC, GStreamer, MPC-HC and similar applications do not count as independent codec oracles
when they simply dispatch into libavcodec, libvpx, dav1d, platform MFTs, or another already-counted
backend. Record the backend identity, not just the application name.

## Families that are now strong enough for aggressive implementation

Use the normal gate (normative/reference vectors + foreign corpus + pixel/frame comparison + our output
accepted by external decoder) for these groups:

- PNG, JPEG, GIF, TIFF, WebP, QOI, OpenEXR;
- JPEG-LS, JPEG 2000/HTJ2K, JPEG XR, AVIF/HEIF;
- DDS/BCn, ASTC, KTX/KTX2, Basis Universal;
- FITS and the already-covered scientific/medical formats where scalar/layout semantics are checked;
- AV1, VP8/VP9, H.264, HEVC, VVC;
- MPEG-1 video, MPEG-2 video, MPEG-4 Part 2, H.263;
- Theora, VC-2/Dirac, CineForm, Hap, DNxHD/DNxHR;
- AVS2 and AVS3 once their conformance corpora are wired into the harness;
- containers already covered by GPAC/Bento4/MKVToolNix/libwebm/Shaka/oggz/MediaConch.

## Formats that still need explicit `oracle-limited` handling

The following classes should not be upgraded to a "two independent engines" confidence level without
new evidence. Some may have usable original binaries or historical samples, but public implementation
independence is currently weak or unproven:

- On2 VP4/VP5/VP6/VP7 generations other than the standardized/libvpx VP8/VP9 lineage;
- Intel Indeo 3/4/5, especially variants for which FFmpeg is the only practical modern decoder;
- RealVideo 1-4 where a runnable original Real/Helix decoder cannot be established;
- TrueMotion 1/2, Escape 124/130 and several other historical VfW/QuickTime codecs;
- LCL MSZH/ZLIB where independent historical codec binaries may exist but are not yet automated;
- Sierra VMD, EA CMV, Interplay MVE and similar game formats unless a genuinely independent game
  engine/tool is identified for the exact codec variant;
- obscure AVI/VfW codecs such as AASC/ASV variants where documentation and independent decoders are
  sparse;
- RealMedia/legacy game containers whose parsing is effectively only exercised by one modern stack.

For these formats the safe implementation recipe is:

1. derive behavior from the original specification/file samples where available;
2. use FFmpeg or the surviving decoder strictly as a behavioral oracle, not as implementation source;
3. search original vendor players/codecs, game engines and archived SDKs for a second executable oracle;
4. add synthetic edge/malformed tests derived from the format grammar;
5. keep the support row marked `oracle-limited` until genuinely independent evidence exists.

## Practical next harness work

Highest-return additions are:

1. add runnable wrappers for CharLS, jxrlib, JasPer/OpenJPH, DirectXTex/Compressonator and CFITSIO;
2. add MPEG-1/2/4 reference-model and H.263 TMN corpus generation/decoding;
3. add libgav1 as a third AV1 decoder and wire AVS2/AVS3 conformance streams;
4. add the Vidvox Hap reference decoder directly to `EncoderOracleTests` and consume its published
   multi-encoder test packs;
5. automate RAD Video Tools/BinkPlayer in an optional Windows/Linux vendor-oracle lane;
6. build a per-codec lineage table for ScummVM before using it as evidence for game codecs;
7. maintain a machine-readable `oracle-limited` list so future PRs cannot accidentally claim full
   verification from two frontends backed by one decoder engine.

The correct end state is therefore not "every format has two tools". It is "every format has the
strongest independently justified oracle set that still exists, and the repository records exactly
where historical reality prevents stronger proof."
