# Image conformance oracles

The image package needs two independent checks, because a reader and writer implemented from the same
interpretation of a format can agree perfectly and both be wrong.

## Two directions of evidence

| Direction | Question | Mechanism |
| --- | --- | --- |
| Writer conformance | Can an unrelated decoder read bytes written by this repository? | `[VerifiedBy]`, `WriterOracleTests`, and `build-oracle-corpus.sh` |
| Reader conformance | Does this repository decode bytes written elsewhere the same way an unrelated decoder does? | `build-reader-oracle-corpus.sh` and `ReaderOracleCorpusTests` |

A tool being listed below is not itself a conformance claim. A claim starts only after the tool has
actually consumed or produced the bytes under test. The generated reader corpus therefore records
both the external writer and the external decoder for each case.

The tools are processes used as behavioural oracles. None is linked into or shipped with the image
libraries, and no implementation source is copied from them. GPL/AGPL tools are consequently useful
here without turning them into dependencies of the LGPL package.

## Oracle catalogue

| Oracle | Reads | Writes | Useful formats / role | Executable(s) |
| --- | :---: | :---: | --- | --- |
| RECOIL | ✅ | — | Retro-computer image formats | `recoil2png` |
| ImageMagick | ✅ | ✅ | Broad raster coverage and neutral source/reference conversion | `magick` |
| XnView | ✅ | ✅ | Broad legacy-format coverage | `nconvert` |
| IrfanView | ✅ | ✅ | Independent Windows legacy-format coverage | `i_view64.exe` |
| FFmpeg | ✅ | ✅ | Mainstream raster, camera and multimedia-adjacent formats | `ffmpeg` |
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

The `ConformanceOracle` enum also retains the narrower existing tools (`dwebp`, `djxl`, OpenJPEG,
libheif, libavif, Ghostscript, LibreOffice, ExifTool, pyembroidery and olefile) because they remain
better evidence than a broad converter where their native format applies.

## Reader conformance corpus

Generate all cases for which the machine has the external tools:

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

## Sources

The command lines and format roles above come from the projects' own documentation or repositories:

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
