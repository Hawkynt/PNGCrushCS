# Image and video conformance oracles

The image and video packages need evidence from outside this repository. A reader and writer
implemented from the same interpretation of a format can agree perfectly and both be wrong.

A large number of programs is not the same thing as a large number of independent implementations.
ImageMagick, libvips, OpenImageIO, GIMP, VLC, GStreamer and similar hosts may delegate a format to the
same codec library. Such programs are still useful interoperability targets, but they count as
separate conformance evidence only when the backend that actually parsed or decoded the bytes is
known to be independent.

## Evidence policy

A tool being listed in `ConformanceOracle` or below is not itself a conformance claim. A claim starts
only after that exact engine has actually consumed or produced the bytes under test.

For a format to be considered strongly verified, use the following evidence where it exists:

| Surface | Minimum useful evidence | Strong evidence |
| --- | --- | --- |
| Image/video reader or decoder | Foreign bytes from one unrelated producer | Official/published vectors plus bytes from a second independent producer; compare decoded pixels/samples |
| Image/video writer or encoder | One unrelated native/reference decoder accepts our bytes | Reference/conformance decoder plus a second independent decoder engine |
| Container demuxer | Foreign files from an unrelated muxer | Reference/spec corpus plus a second parser implementation |
| Container muxer | An unrelated parser can walk the file and recover packets | A second independent parser/demuxer also accepts it, with codec validation performed separately |
| Malformed input handling | Truncated and boundary cases | Published negative vectors, fuzz corpora, size/overflow limits and differential rejection testing |

For standardized codecs, a reference model or official conformance suite is preferable to another
media player. For proprietary and historical formats where only one public implementation exists,
record the implementation as **oracle-limited** rather than pretending that a second wrapper around
the same engine is independent.

Container and codec evidence are deliberately separate. For example, using FFmpeg to demux an AVI
and then feeding the extracted bitstream to `vpxdec` proves the VPx decoder independently, but it does
not provide a second AVI parser. Conversely, `mkvinfo` can prove Matroska structure without proving the
codec carried inside it.

## Independence classes

| Class | Examples | How it counts |
| --- | --- | --- |
| Normative/reference implementation or conformance suite | libpng, JM, HM, VTM, VC-2 conformance software, official vectors | Strongest executable specification evidence |
| Independent codec engine | Wuffs, libspng, TinyEXR, dav1d, libde265, Kvazaar, VVdeC | Independent evidence when it does not delegate to the engine already used |
| Native format implementation | libjpeg-turbo, giflib, libtiff, OpenEXR, QOI reference, libvpx, libaom, libtheora, CineForm SDK | Strong format-specific oracle |
| Independent container parser/muxer | GPAC/MP4Box, Bento4, MKVToolNix, libwebm, oggz, Shaka Packager | Strong container evidence; codec correctness still needs a codec oracle |
| Host / aggregator | ImageMagick, OpenImageIO, libvips, GIMP, VLC, GStreamer | Interoperability evidence; independent only after backend provenance is known |
| Structural inspector | pngcheck, jpeginfo, MediaInfo, MediaConch | Valuable syntax/metadata evidence; not a replacement for decoded-pixel/frame comparison |

## Two directions of image evidence

| Direction | Question | Mechanism |
| --- | --- | --- |
| Writer conformance | Can an unrelated decoder read bytes written by this repository? | `[VerifiedBy]`, `WriterOracleTests`, and `build-oracle-corpus.sh` |
| Reader conformance | Does this repository decode bytes written elsewhere the same way an unrelated decoder does? | `build-reader-oracle-corpus.sh` and `ReaderOracleCorpusTests` |

The generated reader corpus records both the external writer and the external decoder for each case.

The tools are processes used as behavioural oracles. None is linked into or shipped with the image or
video libraries, and no implementation source is copied from them. GPL tools are therefore usable as
behavioural oracles without becoming dependencies of the LGPL package. If implementation code is ever
studied from an incompatible or uncertain license, use the clean-room workflow instead of translating
it.

## Image oracle catalogue

| Oracle | Reads | Writes | Useful formats / role | Executable(s) / harness |
| --- | :---: | :---: | --- | --- |
| libpng | ✅ | ✅ | Official PNG reference library | `pngtest`, `pngvalid`, or a minimal harness |
| libjpeg-turbo / IJG | ✅ | ✅ | JPEG/JFIF native implementation | `djpeg`, `cjpeg`, `jpegtran` |
| giflib | ✅ | ✅ | GIF native implementation | `gif2rgb`, `gifbuild`, `giftext` |
| libtiff | ✅ | ✅ | TIFF native implementation | `tiffinfo`, `tiffcp` |
| OpenEXR | ✅ | ✅ | EXR reference implementation and reference images | OpenEXR utilities / minimal harness |
| libspng | ✅ | ✅ | PNG implementation explicitly separate from libpng | Minimal `spng_decode_image` / encoder harness |
| Wuffs | ✅ | — | Independent BMP, GIF, JPEG, PNG, QOI, TGA, WBMP, WebP and related decoders | `example/convert-to-nia` or minimal harness |
| stb_image | ✅ | — | Small independent mainstream raster decoder | Minimal harness |
| TinyEXR | ✅ | ✅ | Independent EXR implementation; useful against OpenEXR | Example tools / minimal harness |
| QOI reference | ✅ | ✅ | QOI specification/reference implementation | `qoiconv` |
| OpenImageIO | ✅ | ✅ | Broad application-level interoperability; backend-dependent | `oiiotool`, `iconvert` |
| GIMP | ✅ | ✅ | Broad application-level import/export interoperability | batch-mode `gimp` |
| libvips | ✅ | ✅ | Broad application-level interoperability; backend-dependent | `vips` |
| RECOIL | ✅ | — | Retro-computer image formats | `recoil2png` |
| ImageMagick | ✅ | ✅ | Broad raster coverage and neutral source/reference conversion | `magick` |
| XnView | ✅ | ✅ | Broad legacy-format coverage | `nconvert` |
| IrfanView | ✅ | ✅ | Windows legacy-format coverage | `i_view64.exe` |
| FFmpeg | ✅ | ✅ | Multimedia-adjacent still-image formats | `ffmpeg` |
| Deark | ✅ | — | Historic DOS/Windows/Amiga/Mac formats underserved by mainstream converters | `deark` |
| astcenc | ✅ | ✅ | ASTC | `astcenc` |
| KTX-Software | ✅ | ✅ | KTX/KTX2 validation, creation and extraction | `ktx` |
| DjVuLibre | ✅ | ✅ | DjVu | `ddjvu`, `c44`, `cjb2`, `cpaldjvu` |
| libbpg | ✅ | ✅ | BPG | `bpgdec`, `bpgenc` |
| FLIF reference implementation | ✅ | ✅ | FLIF | `flif` |
| jbig2dec / jbig2enc | ✅ | ✅ | JBIG2 | `jbig2dec`, `jbig2` |
| GDAL | ✅ | ✅ | NITF, ENVI and other geospatial/scientific rasters | `gdal_translate` |
| Convert3D / ITK | ✅ | ✅ | NIfTI, NRRD, MetaImage, MRC and related medical/scientific formats | `c3d` |
| Netpbm | ✅ | ✅ | Andrew Toolkit, Group-3 fax and assorted historic bitmap formats | `atktopbm`, `pbmtoatk`, `g3topbm`, `pbmtog3`, ... |
| Ansilove | ✅ | — | ANSI/ASCII art | `ansilove` |
| LibRaw | ✅ | — | Camera raw formats | `dcraw_emu` |
| Krita | ✅ | ✅ | Krita `.kra` | `krita` |
| hp2xx | ✅ | — | HP-GL | `hp2xx` |
| GhostPCL | ✅ | — | PCL/HP-GL/2 | `gpcl6` |
| DCMTK | ✅ | ✅ | DICOM | `dcm2pnm`, `img2dcm` |

The enum also retains narrower specialist tools (`dwebp`, `djxl`, OpenJPEG, libheif, libavif,
Ghostscript, LibreOffice, ExifTool, pyembroidery and olefile) because a native decoder is usually
better evidence than a broad converter for its own format.

### Image confidence targets

The common formats should not rely on the broad-converter cluster alone:

- PNG: libpng + libspng + Wuffs, with PNGSuite/libpng test images where applicable.
- JPEG: libjpeg-turbo + Wuffs, with published/JPEG conformance vectors where available.
- GIF: giflib + Wuffs, including animation/disposal/interlace cases.
- WebP: libwebp + Wuffs, separating lossless, lossy, alpha and animation cases.
- EXR: OpenEXR + TinyEXR; TinyEXR's documented unsupported modes must not be counted as coverage.
- QOI: QOI reference implementation + Wuffs.
- JPEG 2000, JPEG XL, HEIF/AVIF, ASTC/KTX and similar specialist formats: prefer their native SDK or
  reference implementation first, then add a second engine where one exists.

## Video codec oracle catalogue

The video package needs both producer and consumer diversity. These tools cover the standards with the
largest independent ecosystems:

| Codec family | Producer-side oracles | Consumer/reference oracles | Notes |
| --- | --- | --- | --- |
| AV1 | libaom, SVT-AV1, rav1e | libaom, dav1d | Use official AOM vectors as an additional fixed corpus |
| VP8 / VP9 | libvpx | libvpx plus a separately verified decoder engine where practical | Keep WebM container validation separate |
| H.264 / AVC | x264, OpenH264, JM | OpenH264, JM | JM is the standardized reference-software line; use its conformance vectors |
| H.265 / HEVC | x265, Kvazaar, HM | libde265, HM | HM and published HEVC conformance bitstreams are the reference baseline |
| H.266 / VVC | VVenC, VTM | VVdeC, VTM | VTM is reference software; VVenC/VVdeC are separate optimized implementations |
| MPEG-4 Part 2 | Xvid | Xvid, FFmpeg | Cross-check profiles/quarter-pel/GMC separately where supported |
| Theora | libtheora | libtheora, FFmpeg | Xiph reference implementation is the primary native oracle |
| CineForm | CineForm SDK | CineForm SDK, FFmpeg | GoPro released the encoder and decoder SDK |
| VC-2 / Dirac | VC-2 conformance test-case generator | VC-2 reference decoder / bitstream validator | BBC suite covers encoder and decoder conformance procedures |
| Legacy / proprietary | Native/original encoder when obtainable, otherwise foreign corpus | FFmpeg plus a genuinely separate implementation when one exists | Mark formats with only one public engine as oracle-limited |

Do not count a player as a second decoder merely because it has a different executable name. VLC or
GStreamer configured to use libavcodec is the same codec engine as FFmpeg for this purpose.

Likewise, evaluate source lineage per legacy codec. ScummVM can be an excellent independent oracle for
some game formats, but its Smacker decoder explicitly documents that it is based on FFmpeg's Smacker
decoder, so it is not independent Smacker evidence.

## Video container oracle catalogue

| Oracle | Container role | Typical executable(s) |
| --- | --- | --- |
| GPAC / MP4Box | ISO-BMFF/MP4/3GP plus import/export inspection for MPEG-2 TS, AVI, MKV and other media | `MP4Box` |
| Bento4 | Independent ISO-BMFF/MP4 atom/sample parser | `mp4info`, `mp4dump`, `mp4extract` |
| MKVToolNix | Matroska/WebM de-facto reference muxing and inspection | `mkvinfo`, `mkvmerge`, `mkvextract` |
| libwebm | Native WebM/Matroska parser and muxer | `webm_info`, parser/muxer sample tools |
| Shaka Packager | ISO-BMFF, WebM and MPEG-2 TS parser/packager | `packager` |
| oggz / libogg | Ogg inspection, validation and read/write | `oggz validate`, `oggz dump` |
| MediaInfo | Broad independent structural/metadata inspection | `mediainfo` |
| MediaConch | Preservation-oriented implementation/conformance checks | `mediaconch` |
| FFmpeg | Broad baseline demux/mux interoperability | `ffmpeg`, `ffprobe` |

For AVI/RIFF, ASF, FLV, RealMedia and historical/game containers, FFmpeg remains the broadest automated
baseline. Add native/original applications, published corpora or independent parsers whenever they can
be obtained; do not manufacture a second vote by routing another frontend through libavformat.

## Reader conformance corpus

Generate all image cases for which the machine has the external tools:

```sh
bash Tools/parity/build-reader-oracle-corpus.sh all
```

Or generate one family while bringing a new oracle online:

```sh
bash Tools/parity/build-reader-oracle-corpus.sh astc
bash Tools/parity/build-reader-oracle-corpus.sh djvu
bash Tools/parity/build-reader-oracle-corpus.sh c3d
```

The script accepts explicit executable paths through environment variables such as `ASTCENC`, `KTX`,
`C44`, `DDJVU`, `BPGENC`, `BPGDEC`, `FLIF`, `JBIG2ENC`, `JBIG2DEC`, `GDAL_TRANSLATE`, `C3D`, `KRITA`,
`IMG2DCM` and `DCM2PNM`. If a variable is not set, the conventional executable name is searched on
`PATH`. Missing tools are skipped rather than converted into false failures.

Each manifest row contains:

```text
external-writer    external-decoder    ImageFormat    encoded-file    reference-png    tolerance
```

Run the comparison with:

```sh
READER_ORACLE_CORPUS=/tmp/pngcrush-parity/reader-oracle-corpus \
  dotnet test Tests/Hawkynt.FileFormats.Images.Tests \
  --filter ReaderOracleCorpus
```

The test decodes the foreign file with the exact registry entry named by the manifest, normalizes both
our result and the external reference to BGRA32, and compares every channel. Lossless formats use zero
tolerance. Lossy formats record a deliberately small per-channel tolerance in the manifest.

## What remains before "whole domain" is a safe claim

The catalogue is now broad enough to implement the mainstream standardized image and video families
without depending on one external codebase. It is not possible to make the entire historical domain
safe merely by installing more programs. Some proprietary and game formats have only one surviving
public decoder, no normative specification, or implementations descended from the same reverse
engineering work.

For those formats, require all obtainable evidence: original files from multiple producers/versions,
original application output when runnable, independently derived format notes, malformed/truncated
samples, and at least one external decoder. If there is no independent second engine, keep the status
oracle-limited and do not turn a successful self-round-trip into a conformance claim.

## Licensing boundary

No source from these tools is being copied by this oracle work. Notable licenses checked while adding
the new candidates include Wuffs (MIT/Apache-2.0), libspng (BSD-2-Clause), TinyEXR (BSD-3-Clause), QOI
reference (MIT), Kvazaar (BSD-3-Clause), VVenC/VVdeC (Clear BSD), CineForm SDK (Apache-2.0/MIT), BBC
VC-2 conformance software (GPL-3.0), ScummVM (GPL-3.0) and Bento4's GPL/commercial model. GPL or
otherwise incompatible code is suitable as an executable behavioural oracle, but must not be copied
or closely translated into the LGPL implementation.

## Sources

Primary project/specification sources used for the catalogue and independence policy:

- libpng: https://github.com/pnggroup/libpng and https://www.libpng.org/pub/png/libpng.html
- libjpeg-turbo: https://github.com/libjpeg-turbo/libjpeg-turbo
- libspng: https://github.com/randy408/libspng
- Wuffs: https://github.com/google/wuffs
- stb: https://github.com/nothings/stb
- TinyEXR: https://github.com/syoyo/tinyexr
- QOI reference: https://github.com/phoboslab/qoi
- OpenEXR: https://github.com/AcademySoftwareFoundation/openexr
- OpenImageIO: https://openimageio.readthedocs.io/
- Deark: https://github.com/jsummers/deark
- astcenc: https://github.com/ARM-software/astc-encoder
- KTX-Software: https://github.com/KhronosGroup/KTX-Software
- DjVuLibre: https://djvu.sourceforge.net/
- libbpg: https://bellard.org/bpg/
- FLIF: https://github.com/FLIF-hub/FLIF
- jbig2dec: https://github.com/ArtifexSoftware/jbig2dec
- jbig2enc: https://github.com/agl/jbig2enc
- GDAL: https://gdal.org/
- Convert3D: https://www.itksnap.org/pmwiki/pmwiki.php?n=Convert3D.Convert3D
- Netpbm: https://netpbm.sourceforge.net/
- Ansilove: https://github.com/ansilove/ansilove
- LibRaw: https://www.libraw.org/
- Krita: https://krita.org/
- hp2xx: https://github.com/dgtlrift/hp2xx
- GhostPDL: https://ghostscript.com/
- DCMTK: https://dicom.offis.de/dcmtk.php.en
- H.264 JM and conformance material: https://avc.hhi.fraunhofer.de/
- HEVC HM and conformance material: https://hevc.hhi.fraunhofer.de/
- VTM: https://vcgit.hhi.fraunhofer.de/jvet/VVCSoftware_VTM
- VVenC: https://github.com/fraunhoferhhi/vvenc
- VVdeC: https://github.com/fraunhoferhhi/vvdec
- Kvazaar: https://github.com/ultravideo/kvazaar
- rav1e: https://github.com/xiph/rav1e
- CineForm SDK: https://github.com/gopro/cineform-sdk
- VC-2 conformance software: https://github.com/bbc/vc2_conformance
- GPAC / MP4Box: https://gpac.io/
- Bento4: https://www.bento4.com/
- MKVToolNix: https://www.matroska.org/downloads/mkvtoolnix.html
- libwebm: https://www.webmproject.org/code/
- Shaka Packager: https://github.com/shaka-project/shaka-packager
- oggz / libogg: https://www.xiph.org/oggz/ and https://www.xiph.org/ogg/
- MediaInfo / MediaConch: https://mediaarea.net/
- ScummVM lineage check for Smacker: https://github.com/scummvm/scummvm/blob/master/video/smk_decoder.cpp
