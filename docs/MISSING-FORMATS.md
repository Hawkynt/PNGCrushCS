# Missing formats

> Every image and video format known to be missing from this repository, or registered but only
> partly read, with one GitHub issue each — grouped by category.

Both format packages claim their whole domain, so a format that is not here is a tracked gap rather
than a decision. This page is the index of those gaps. Each issue is written to be ready for
implementation: it identifies the format, states its full scope, links the specification, reference
implementations with their licence and sourcing rung, an oracle and a sample corpus, and lists the
test cases and the quality gate. That gate is the same everywhere: the format is read and written
completely (every mode, bit depth, compression and metadata block it defines), verified against an
independent oracle or real files, documented in the package README, and usable in `Crush.Viewer` to
load, save and convert.

The list was compiled on 2026-11-23 by comparing the registries against FFmpeg n9.0.2 (`-codecs`,
`-formats`), ImageMagick 7.1.2, RECOIL, XnView's `Formats.txt`, GDAL's raster drivers, LibRaw,
Netpbm, dexvert, NihAV, librempeg, the Archive Team file-format wiki, MultimediaWiki and this
repository's own gap records ([`coverage-gap.md`](../Tools/parity/coverage-gap.md),
[`format-investigations.md`](../Hawkynt.FileFormats.Images/format-investigations.md),
[`codec-investigations.md`](../Hawkynt.FileFormats.Video/codec-investigations.md)). When an issue
is closed, its row leaves this page in the same change.

Three kinds of entry appear besides a plain missing format. **Partial** entries are formats the registry
claims whose reader refuses part of what the format defines (compressed DNG, compressed DICOM transfer
syntaxes, lossless and arithmetic JPEG, VC-1 Advanced Profile, …). **Encoder** entries are video codecs
that decode but cannot be written. **Blocked** entries were investigated earlier and stopped; their
issue quotes the recorded blocker and the route around it.

Video issues depend on [#512](https://github.com/Hawkynt/PNGCrushCS/issues/512), which gives the viewer a video path at all.

## 📊 Summary

| Category | Tracking issue | Images | Codecs | Containers | Encoders | Total |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| [Modern & standard still-image codecs](#modern--standard-still-image-codecs) | [#513](https://github.com/Hawkynt/PNGCrushCS/issues/513) | 63 | 0 | 0 | 0 | 63 |
| [Camera RAW & sensor data](#camera-raw--sensor-data) | [#514](https://github.com/Hawkynt/PNGCrushCS/issues/514) | 26 | 0 | 0 | 0 | 26 |
| [Geospatial & remote sensing](#geospatial--remote-sensing) | [#515](https://github.com/Hawkynt/PNGCrushCS/issues/515) | 83 | 0 | 0 | 0 | 83 |
| [Medical & scientific imaging](#medical--scientific-imaging) | [#516](https://github.com/Hawkynt/PNGCrushCS/issues/516) | 42 | 0 | 0 | 0 | 42 |
| [GPU textures & game assets](#gpu-textures--game-assets) | [#517](https://github.com/Hawkynt/PNGCrushCS/issues/517) | 40 | 0 | 0 | 0 | 40 |
| [Retro computer & legacy platforms](#retro-computer--legacy-platforms) | [#518](https://github.com/Hawkynt/PNGCrushCS/issues/518) | 102 | 0 | 0 | 0 | 102 |
| [Vector, document & print](#vector-document--print) | [#519](https://github.com/Hawkynt/PNGCrushCS/issues/519) | 92 | 0 | 0 | 0 | 92 |
| [Fonts, text-mode & teletext](#fonts-text-mode--teletext) | [#520](https://github.com/Hawkynt/PNGCrushCS/issues/520) | 61 | 0 | 0 | 0 | 61 |
| [Editor & application native](#editor--application-native) | [#521](https://github.com/Hawkynt/PNGCrushCS/issues/521) | 49 | 0 | 0 | 0 | 49 |
| [Embroidery & industrial](#embroidery--industrial) | [#522](https://github.com/Hawkynt/PNGCrushCS/issues/522) | 20 | 0 | 0 | 0 | 20 |
| [Standard & broadcast video](#standard--broadcast-video) | [#523](https://github.com/Hawkynt/PNGCrushCS/issues/523) | 0 | 37 | 16 | 0 | 53 |
| [Game & multimedia-title video](#game--multimedia-title-video) | [#524](https://github.com/Hawkynt/PNGCrushCS/issues/524) | 0 | 48 | 201 | 6 | 255 |
| [Legacy PC & capture-card video](#legacy-pc--capture-card-video) | [#525](https://github.com/Hawkynt/PNGCrushCS/issues/525) | 0 | 54 | 36 | 2 | 92 |
| [Screen-capture video](#screen-capture-video) | [#526](https://github.com/Hawkynt/PNGCrushCS/issues/526) | 0 | 16 | 0 | 9 | 25 |
| [Lossless & uncompressed video](#lossless--uncompressed-video) | [#527](https://github.com/Hawkynt/PNGCrushCS/issues/527) | 0 | 14 | 0 | 2 | 16 |
| [Cinema camera RAW video](#cinema-camera-raw-video) | [#528](https://github.com/Hawkynt/PNGCrushCS/issues/528) | 0 | 6 | 7 | 0 | 13 |
| [Elementary streams & streaming manifests](#elementary-streams--streaming-manifests) | [#529](https://github.com/Hawkynt/PNGCrushCS/issues/529) | 0 | 0 | 18 | 0 | 18 |
| **All** | | **578** | **175** | **278** | **19** | **1050** |

Each issue carries the labels `cat: <category>`, its kind (`image-format`, `video-codec`, `video-container`, `video-encoder`), `priority: high|medium|low`, and `documented-blocker` where that applies.

## Modern & standard still-image codecs

JPEG/JPEG 2000/HEIF variants, wavelet, HDR and research still-image codecs. Tracked in [#513](https://github.com/Hawkynt/PNGCrushCS/issues/513).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Apple CgBI ('iPhone-optimised') PNG | Image | `.png` | read + write | missing | medium | [#530](https://github.com/Hawkynt/PNGCrushCS/issues/530) |
| AVIF image sequence (animated AVIF, 'avis') | Image | `.avifs`, `.avif` | read + write | missing | medium | [#531](https://github.com/Hawkynt/PNGCrushCS/issues/531) |
| BPG transform-coded (HEVC) pictures | Image | `.bpg` | read + write | blocked | medium | [#532](https://github.com/Hawkynt/PNGCrushCS/issues/532) |
| HEIF/HEIC with HEVC range-extension / SCC tools and >12-bit samples | Image | `.heic`, `.heif`, `.hif` | read + write | blocked | medium | [#533](https://github.com/Hawkynt/PNGCrushCS/issues/533) |
| High-Throughput JPEG 2000 (HTJ2K, Part 15) | Image | `.jph`, `.jhc` | read + write | missing | medium | [#534](https://github.com/Hawkynt/PNGCrushCS/issues/534) |
| JPEG XL remaining sub-features (edge-preserving filter; lossy multi-group alpha) | Image | `.jxl` | read + write | blocked | medium | [#535](https://github.com/Hawkynt/PNGCrushCS/issues/535) |
| JPEG XS | Image | `.jxs` | read + write | missing | medium | [#536](https://github.com/Hawkynt/PNGCrushCS/issues/536) |
| Lossless JPEG (ITU T.81 process 14, SOF3/SOF7) | Image | `.jpg`, `.ljpg`, `.ljpeg` | read + write | missing | medium | [#537](https://github.com/Hawkynt/PNGCrushCS/issues/537) |
| AOL ART (Johnson-Grace compressed image) | Image | `.art` | read + write | missing | low | [#538](https://github.com/Hawkynt/PNGCrushCS/issues/538) |
| BCIF | Image | `.bcif` | read + write | missing | low | [#539](https://github.com/Hawkynt/PNGCrushCS/issues/539) |
| BEF (Unified Color HDR) | Image | `.bef` | read + write | missing | low | [#540](https://github.com/Hawkynt/PNGCrushCS/issues/540) |
| BMF (Dmitry Shkarin) | Image | `.bmf` | read + write | missing | low | [#541](https://github.com/Hawkynt/PNGCrushCS/issues/541) |
| Brunsli (lossless JPEG repacking) | Image | `.brn` | read + write | missing | low | [#542](https://github.com/Hawkynt/PNGCrushCS/issues/542) |
| BTPC and APT (John Robinson) | Image | `.btpc`, `.apt` | read + write | missing | low | [#543](https://github.com/Hawkynt/PNGCrushCS/issues/543) |
| Cartesian Perceptual Compression (CPC) | Image | `.cpc`, `.cpi` | read + write | missing | low | [#544](https://github.com/Hawkynt/PNGCrushCS/issues/544) |
| DCT (Crash Barrier) | Image | `.dct` | read + write | missing | low | [#545](https://github.com/Hawkynt/PNGCrushCS/issues/545) |
| Deep Zoom (DZI) and Zoomify (ZIF, PFF) tile pyramids | Image | `.dzi`, `.zif`, `.pff` | read + write | missing | low | [#546](https://github.com/Hawkynt/PNGCrushCS/issues/546) |
| FIASCO - Fractal Image And Sequence Codec | Image | `.fco`, `.wfa` | read + write | missing | low | [#547](https://github.com/Hawkynt/PNGCrushCS/issues/547) |
| Fractal Image Format (Iterated Systems FIF) | Image | `.fif`, `.fi` | read + write | blocked | low | [#548](https://github.com/Hawkynt/PNGCrushCS/issues/548) |
| FUIF - Free Universal Image Format | Image | `.fuif` | read + write | missing | low | [#549](https://github.com/Hawkynt/PNGCrushCS/issues/549) |
| GIFBLAST (GFB) | Image | `.gfb` | read + write | missing | low | [#550](https://github.com/Hawkynt/PNGCrushCS/issues/550) |
| Gifpress compressed GIF (.gps) | Image | `.gps` | read + write | missing | low | [#551](https://github.com/Hawkynt/PNGCrushCS/issues/551) |
| HEIF image sequence (HEVC/AVC track, .heics/.avcs) | Image | `.heics`, `.hevcs`, `.avcs` | read + write | missing | low | [#552](https://github.com/Hawkynt/PNGCrushCS/issues/552) |
| hipix (HPX) | Image | `.hpx` | read + write | missing | low | [#553](https://github.com/Hawkynt/PNGCrushCS/issues/553) |
| J Wavelet Image Codec (WIC) | Image | `.wic` | read + write | blocked | low | [#554](https://github.com/Hawkynt/PNGCrushCS/issues/554) |
| JPEG 2000 in HEIF (HEJ2, ISO/IEC 15444-16) | Image | `.hej2`, `.heif` | read + write | missing | low | [#555](https://github.com/Hawkynt/PNGCrushCS/issues/555) |
| JPEG AI (ISO/IEC 6048) | Image | `.jai` | read + write | missing | low | [#556](https://github.com/Hawkynt/PNGCrushCS/issues/556) |
| JPEG arithmetic coding (SOF9/SOF10/SOF11) | Image | `.jpg`, `.jpeg` | read + write | missing | low | [#557](https://github.com/Hawkynt/PNGCrushCS/issues/557) |
| JPEG Pleno light field (ISO/IEC 21794-2) | Image | `.jpl` | read + write | missing | low | [#558](https://github.com/Hawkynt/PNGCrushCS/issues/558) |
| JPEG XR planar alpha and other component counts | Image | `.jxr`, `.wdp`, `.hdp` | read + write | blocked | low | [#559](https://github.com/Hawkynt/PNGCrushCS/issues/559) |
| JPEG XT (ISO/IEC 18477, incl. JPEG-HDR) | Image | `.jpg`, `.jxt` | read + write | missing | low | [#560](https://github.com/Hawkynt/PNGCrushCS/issues/560) |
| KIFF (Kt Interchange File Format) | Image | `.kif`, `.kiff` | read + write | missing | low | [#561](https://github.com/Hawkynt/PNGCrushCS/issues/561) |
| LEADTOOLS ABC (Advanced Bitonal Compression) | Image | `.abc` | read + write | missing | low | [#562](https://github.com/Hawkynt/PNGCrushCS/issues/562) |
| LEADTOOLS CMP (LEAD compressed image) | Image | `.cmp` | read + write | missing | low | [#563](https://github.com/Hawkynt/PNGCrushCS/issues/563) |
| Lepton (Dropbox lossless JPEG recompression) | Image | `.lep` | read + write | missing | low | [#564](https://github.com/Hawkynt/PNGCrushCS/issues/564) |
| libgd GD image (.gd) | Image | `.gd` | read + write | missing | low | [#565](https://github.com/Hawkynt/PNGCrushCS/issues/565) |
| Lightning Strike (.cod), Corel Wavelet (.wi), Elecard Wavelet (.wlt), J Wavelet (.wic), WaveL IWC | Image | `.cod`, `.wi`, `.wvl`, `.wlt`, `.wic`, `.iwc` | read + write | missing | low | [#566](https://github.com/Hawkynt/PNGCrushCS/issues/566) |
| LuraWave (LWF) | Image | `.lwf` | read + write | blocked | low | [#567](https://github.com/Hawkynt/PNGCrushCS/issues/567) |
| Magick Persistent Cache (MPC) | Image | `.mpc`, `.cache` | read + write | missing | low | [#568](https://github.com/Hawkynt/PNGCrushCS/issues/568) |
| Naive Image Animation (NIA) | Image | `.nia` | read + write | missing | low | [#569](https://github.com/Hawkynt/PNGCrushCS/issues/569) |
| packJPG / packPNM / packARI image recompression | Image | `.pjg`, `.ppn` | read + write | missing | low | [#570](https://github.com/Hawkynt/PNGCrushCS/issues/570) |
| PCIF | Image | `.pcif` | read + write | missing | low | [#571](https://github.com/Hawkynt/PNGCrushCS/issues/571) |
| Pegasus PIC (PIC/PIC2 JPEG variants) | Image | `.pic` | read + write | missing | low | [#572](https://github.com/Hawkynt/PNGCrushCS/issues/572) |
| PFS - Portable Floatmap Stream (pfstools) | Image | `.pfs` | read + write | missing | low | [#573](https://github.com/Hawkynt/PNGCrushCS/issues/573) |
| PGF - Progressive Graphics File (libpgf) | Image | `.pgf` | read + write | missing | low | [#574](https://github.com/Hawkynt/PNGCrushCS/issues/574) |
| PGMYUV | Image | `.pgmyuv` | read + write | missing | low | [#575](https://github.com/Hawkynt/PNGCrushCS/issues/575) |
| Pick Ax (PAX) | Image | `.pax` | read + write | blocked | low | [#576](https://github.com/Hawkynt/PNGCrushCS/issues/576) |
| Piecewise-Constant image model (PWC) | Image | `.pwc` | read + write | blocked | low | [#577](https://github.com/Hawkynt/PNGCrushCS/issues/577) |
| PIK (Google) | Image | `.pik` | read + write | missing | low | [#578](https://github.com/Hawkynt/PNGCrushCS/issues/578) |
| QOIR (Quite OK Image Format, Revised) | Image | `.qoir` | read + write | missing | low | [#579](https://github.com/Hawkynt/PNGCrushCS/issues/579) |
| RK Image (RKI) | Image | `.rki` | read + write | missing | low | [#580](https://github.com/Hawkynt/PNGCrushCS/issues/580) |
| STiNG (STN) resolution-on-demand | Image | `.stn` | read + write | missing | low | [#581](https://github.com/Hawkynt/PNGCrushCS/issues/581) |
| Tencent TAP | Image | `.tap` | read + write | missing | low | [#582](https://github.com/Hawkynt/PNGCrushCS/issues/582) |
| TIFF LogLuv / SGILog and PixarLog encodings | Image | `.tif`, `.tiff` | read + write | missing | low | [#583](https://github.com/Hawkynt/PNGCrushCS/issues/583) |
| TIFF with JBIG, LZMA, ZSTD, WebP, LERC, JPEG XL or JPEG 2000 compression | Image | `.tif`, `.tiff` | read + write | missing | low | [#584](https://github.com/Hawkynt/PNGCrushCS/issues/584) |
| UHIC | Image | `.uhi` | read + write | missing | low | [#585](https://github.com/Hawkynt/PNGCrushCS/issues/585) |
| Uncompressed HEIF (ISO/IEC 23001-17 'unci') | Image | `.heif` | read + write | missing | low | [#586](https://github.com/Hawkynt/PNGCrushCS/issues/586) |
| V-Ray image (.vrimg) | Image | `.vrimg` | read + write | missing | low | [#587](https://github.com/Hawkynt/PNGCrushCS/issues/587) |
| VVC (H.266) in HEIF | Image | `.heif`, `.vvic` | read + write | missing | low | [#588](https://github.com/Hawkynt/PNGCrushCS/issues/588) |
| WaveL Image Wavelet Codec (IWC) | Image | `.iwc` | read + write | blocked | low | [#589](https://github.com/Hawkynt/PNGCrushCS/issues/589) |
| WebP 2 (experimental) | Image | `.wp2` | read + write | missing | low | [#590](https://github.com/Hawkynt/PNGCrushCS/issues/590) |
| WhyPic (YPC) | Image | `.ypc` | read + write | blocked | low | [#591](https://github.com/Hawkynt/PNGCrushCS/issues/591) |
| Zpng | Image | `.zpng` | read + write | missing | low | [#592](https://github.com/Hawkynt/PNGCrushCS/issues/592) |

## Camera RAW & sensor data

Digital still/cinema camera raw, scanner raw, thermal and light-field captures. Tracked in [#514](https://github.com/Hawkynt/PNGCrushCS/issues/514).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Canon CR3 CRX sensor codec | Image | `.cr3`, `.crm` | read + write | blocked | high | [#593](https://github.com/Hawkynt/PNGCrushCS/issues/593) |
| DNG compressed / tiled / linear raw (lossless JPEG, lossy JPEG, Deflate, JPEG XL) | Image | `.dng` | read + write | missing | high | [#594](https://github.com/Hawkynt/PNGCrushCS/issues/594) |
| Fujifilm RAF compressed raw (lossless/lossy compressed, X-Trans) | Image | `.raf` | read + write | missing | high | [#595](https://github.com/Hawkynt/PNGCrushCS/issues/595) |
| Olympus/OM System ORF compressed sensor data | Image | `.orf`, `.ori` | read + write | missing | high | [#596](https://github.com/Hawkynt/PNGCrushCS/issues/596) |
| Panasonic RW2 / Leica RWL sensor codecs (v4-v8) | Image | `.rw2`, `.rwl`, `.raw` | read + write | missing | high | [#597](https://github.com/Hawkynt/PNGCrushCS/issues/597) |
| GoPro GPR (VC-5 raw) | Image | `.gpr` | read + write | missing | medium | [#598](https://github.com/Hawkynt/PNGCrushCS/issues/598) |
| Hasselblad / Imacon FFF raw | Image | `.fff` | read + write | missing | medium | [#599](https://github.com/Hawkynt/PNGCrushCS/issues/599) |
| Nikon NEF High Efficiency (HE / HE*) raw | Image | `.nef` | read + write | missing | medium | [#600](https://github.com/Hawkynt/PNGCrushCS/issues/600) |
| Pentax PEF / Pentax DNG Huffman compression (65535) | Image | `.pef`, `.ptx` | read + write | missing | medium | [#601](https://github.com/Hawkynt/PNGCrushCS/issues/601) |
| Phase One IIQ sensor codec (IIQ L/S, L16) and CAP/EIP | Image | `.iiq`, `.cap`, `.eip` | read + write | missing | medium | [#602](https://github.com/Hawkynt/PNGCrushCS/issues/602) |
| Samsung SRW sensor compressions (v0/v1/v2) | Image | `.srw` | read + write | missing | medium | [#603](https://github.com/Hawkynt/PNGCrushCS/issues/603) |
| Sigma X3F Foveon sensor data (TRUE / Huffman storages) | Image | `.x3f` | read + write | blocked | medium | [#604](https://github.com/Hawkynt/PNGCrushCS/issues/604) |
| Apple QuickTake 100/150 (QTK) | Image | `.qtk` | read + write | missing | low | [#605](https://github.com/Hawkynt/PNGCrushCS/issues/605) |
| ARRIRAW (ARI) | Image | `.ari` | read + write | missing | low | [#606](https://github.com/Hawkynt/PNGCrushCS/issues/606) |
| Casio BAY raw | Image | `.bay` | read + write | missing | low | [#607](https://github.com/Hawkynt/PNGCrushCS/issues/607) |
| Cintel RAW (CRI) | Image | `.cri` | read + write | missing | low | [#608](https://github.com/Hawkynt/PNGCrushCS/issues/608) |
| FLIR radiometric thermal images (FFF records, radiometric JPEG, FPF) | Image | `.jpg`, `.fff`, `.fpf`, `.seq`, `.csq` | read + write | missing | low | [#609](https://github.com/Hawkynt/PNGCrushCS/issues/609) |
| Kodak raw codecs (DCR/KDC 65000, RADC, DC120, KDC) | Image | `.dcr`, `.kdc`, `.dcs`, `.k25` | read + write | missing | low | [#610](https://github.com/Hawkynt/PNGCrushCS/issues/610) |
| KOLOR Raw (KRO) | Image | `.kro` | read + write | missing | low | [#611](https://github.com/Hawkynt/PNGCrushCS/issues/611) |
| Logitech Fotoman PXN | Image | `.pxn` | read + write | missing | low | [#612](https://github.com/Hawkynt/PNGCrushCS/issues/612) |
| Lytro light field (LFP/LFR) | Image | `.lfp`, `.lfr` | read + write | missing | low | [#613](https://github.com/Hawkynt/PNGCrushCS/issues/613) |
| Minolta RD175 raw (MDC) | Image | `.mdc` | read + write | missing | low | [#614](https://github.com/Hawkynt/PNGCrushCS/issues/614) |
| Rawzor compressed raw (RWZ) | Image | `.rwz` | read + write | missing | low | [#615](https://github.com/Hawkynt/PNGCrushCS/issues/615) |
| Sinar raw (STI / CaptureShop CS1) | Image | `.sti`, `.cs1` | read + write | missing | low | [#616](https://github.com/Hawkynt/PNGCrushCS/issues/616) |
| Sony ARQ (pixel-shift multi-shot composite) | Image | `.arq` | read + write | missing | low | [#617](https://github.com/Hawkynt/PNGCrushCS/issues/617) |
| STJ stereoscopic image | Image | `.stj` | read + write | missing | low | [#618](https://github.com/Hawkynt/PNGCrushCS/issues/618) |

## Geospatial & remote sensing

GIS rasters, elevation models, SAR and planetary imagery. Tracked in [#515](https://github.com/Hawkynt/PNGCrushCS/issues/515).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| ERDAS IMAGINE (HFA .img) | Image | `.img`, `.ige`, `.rrd`, `.aux` | read + write | missing | high | [#619](https://github.com/Hawkynt/PNGCrushCS/issues/619) |
| Arc/Info ASCII Grid (AAIGrid) | Image | `.asc`, `.grd` | read + write | missing | medium | [#620](https://github.com/Hawkynt/PNGCrushCS/issues/620) |
| DTED (Digital Terrain Elevation Data) | Image | `.dt0`, `.dt1`, `.dt2` | read + write | missing | medium | [#621](https://github.com/Hawkynt/PNGCrushCS/issues/621) |
| ECW (ER Mapper/ERDAS Enhanced Compression Wavelet, real codec) | Image | `.ecw` | read + write | missing | medium | [#622](https://github.com/Hawkynt/PNGCrushCS/issues/622) |
| ESRI BIL/BIP/BSQ (.hdr labelled raw) | Image | `.bil`, `.bip`, `.bsq`, `.hdr`, `.flt` | read + write | missing | medium | [#623](https://github.com/Hawkynt/PNGCrushCS/issues/623) |
| GRIB 1 / GRIB 2 (WMO gridded binary) | Image | `.grb`, `.grib`, `.grb2`, `.grib2` | read + write | missing | medium | [#624](https://github.com/Hawkynt/PNGCrushCS/issues/624) |
| HDF5 image datasets (incl. HDF5 Image & Palette spec, netCDF-4, HDF-EOS5) | Image | `.h5`, `.hdf5`, `.he5`, `.nc` | read + write | missing | medium | [#625](https://github.com/Hawkynt/PNGCrushCS/issues/625) |
| MrSID (Multi-resolution Seamless Image Database) | Image | `.sid` | read + write | blocked | medium | [#626](https://github.com/Hawkynt/PNGCrushCS/issues/626) |
| NASA PDS4 (XML label + array) | Image | `.xml`, `.lblx`, `.img`, `.dat` | read + write | missing | medium | [#627](https://github.com/Hawkynt/PNGCrushCS/issues/627) |
| netCDF raster (classic CDF-1/2/5) | Image | `.nc`, `.cdf`, `.nc4` | read + write | missing | medium | [#628](https://github.com/Hawkynt/PNGCrushCS/issues/628) |
| NITF 2.0 / NSIF 1.0 and compressed image segments (C1/C3/C4/C5/C8/M3...) | Image | `.ntf`, `.nitf`, `.nsf` | read + write | missing | medium | [#629](https://github.com/Hawkynt/PNGCrushCS/issues/629) |
| SRTM HGT | Image | `.hgt` | read + write | missing | medium | [#630](https://github.com/Hawkynt/PNGCrushCS/issues/630) |
| USGS ISIS3 cube | Image | `.cub`, `.lbl` | read + write | missing | medium | [#631](https://github.com/Hawkynt/PNGCrushCS/issues/631) |
| ACE2 global DEM | Image | `.ace2` | read + write | missing | low | [#632](https://github.com/Hawkynt/PNGCrushCS/issues/632) |
| ADRG (ARC Digitized Raster Graphics) | Image | `.gen`, `.thf`, `.img` | read + write | missing | low | [#633](https://github.com/Hawkynt/PNGCrushCS/issues/633) |
| AIRSAR polarimetric | Image | `.dat`, `.stk` | read + write | missing | low | [#634](https://github.com/Hawkynt/PNGCrushCS/issues/634) |
| Arc/Info Binary Grid (AIG coverage) | Image | `.adf` | read + write | missing | low | [#635](https://github.com/Hawkynt/PNGCrushCS/issues/635) |
| ASCII gridded XYZ | Image | `.xyz`, `.csv`, `.txt` | read + write | missing | low | [#636](https://github.com/Hawkynt/PNGCrushCS/issues/636) |
| BAG (Bathymetry Attributed Grid) | Image | `.bag` | read + write | missing | low | [#637](https://github.com/Hawkynt/PNGCrushCS/issues/637) |
| BYN (NRCan geoid) | Image | `.byn`, `.err` | read + write | missing | low | [#638](https://github.com/Hawkynt/PNGCrushCS/issues/638) |
| CEOS image (optical/SAR leader+imagery files) | Image | `.dat`, `.img`, `.ldr`, `.trl`, `.vol` | read + write | missing | low | [#639](https://github.com/Hawkynt/PNGCrushCS/issues/639) |
| Convair PolGASP (CPG) | Image | `.img`, `.hdr` | read + write | missing | low | [#640](https://github.com/Hawkynt/PNGCrushCS/issues/640) |
| Daylon Leveller heightfield | Image | `.ter` | read + write | missing | low | [#641](https://github.com/Hawkynt/PNGCrushCS/issues/641) |
| DRDC COASP SAR | Image | `.hdr` | read + write | missing | low | [#642](https://github.com/Hawkynt/PNGCrushCS/issues/642) |
| ENVISAT image product (.N1) | Image | `.n1` | read + write | missing | low | [#643](https://github.com/Hawkynt/PNGCrushCS/issues/643) |
| EOSAT FAST format (Landsat TM/IRS) | Image | `.fst`, `.dat` | read + write | missing | low | [#644](https://github.com/Hawkynt/PNGCrushCS/issues/644) |
| ER Mapper ERS (raster dataset header) | Image | `.ers` | read + write | missing | low | [#645](https://github.com/Hawkynt/PNGCrushCS/issues/645) |
| ERDAS 7.x LAN / GIS | Image | `.lan`, `.gis` | read + write | missing | low | [#646](https://github.com/Hawkynt/PNGCrushCS/issues/646) |
| Erdas Imagine Raw (EIR) | Image | `.hdr` | read + write | missing | low | [#647](https://github.com/Hawkynt/PNGCrushCS/issues/647) |
| FARSITE v4 landscape (LCP) | Image | `.lcp` | read + write | missing | low | [#648](https://github.com/Hawkynt/PNGCrushCS/issues/648) |
| Generic Binary (.hdr labelled) | Image | `.hdr` | read + write | missing | low | [#649](https://github.com/Hawkynt/PNGCrushCS/issues/649) |
| Generic Tagged Arrays (GTA) | Image | `.gta` | read + write | missing | low | [#650](https://github.com/Hawkynt/PNGCrushCS/issues/650) |
| Golden Software Surfer 6 binary grid (GSBG) | Image | `.grd` | read + write | missing | low | [#651](https://github.com/Hawkynt/PNGCrushCS/issues/651) |
| Golden Software Surfer 7 binary grid (GS7BG) | Image | `.grd` | read + write | missing | low | [#652](https://github.com/Hawkynt/PNGCrushCS/issues/652) |
| Golden Software Surfer ASCII grid (GSAG) | Image | `.grd` | read + write | missing | low | [#653](https://github.com/Hawkynt/PNGCrushCS/issues/653) |
| GRASS ASCII Grid | Image | `.asc`, `.txt` | read + write | missing | low | [#654](https://github.com/Hawkynt/PNGCrushCS/issues/654) |
| GSC Geogrid | Image | `.gsc` | read + write | missing | low | [#655](https://github.com/Hawkynt/PNGCrushCS/issues/655) |
| GXF (Grid eXchange File) | Image | `.gxf` | read + write | missing | low | [#656](https://github.com/Hawkynt/PNGCrushCS/issues/656) |
| HDF4 raster (SDS / GR / RIS8/RIS24) | Image | `.hdf`, `.h4`, `.hdf4` | read + write | missing | low | [#657](https://github.com/Hawkynt/PNGCrushCS/issues/657) |
| HF2 / HFZ heightfield | Image | `.hf2`, `.hfz`, `.hf2.gz` | read + write | missing | low | [#658](https://github.com/Hawkynt/PNGCrushCS/issues/658) |
| IDRISI raster (RST/RDC) | Image | `.rst`, `.rdc` | read + write | missing | low | [#659](https://github.com/Hawkynt/PNGCrushCS/issues/659) |
| ILWIS raster map (.mpr / .mpl) | Image | `.mpr`, `.mpl`, `.mp#` | read + write | missing | low | [#660](https://github.com/Hawkynt/PNGCrushCS/issues/660) |
| ISCE raster | Image | `.xml` | read + write | missing | low | [#661](https://github.com/Hawkynt/PNGCrushCS/issues/661) |
| ISG (International Service for the Geoid) grid | Image | `.isg` | read + write | missing | low | [#662](https://github.com/Hawkynt/PNGCrushCS/issues/662) |
| Japanese DEM (.mem) | Image | `.mem` | read + write | missing | low | [#663](https://github.com/Hawkynt/PNGCrushCS/issues/663) |
| KEA (HDF5-based GDAL raster) | Image | `.kea` | read + write | missing | low | [#664](https://github.com/Hawkynt/PNGCrushCS/issues/664) |
| Meteosat Second Generation native (.nat) | Image | `.nat` | read + write | missing | low | [#665](https://github.com/Hawkynt/PNGCrushCS/issues/665) |
| MiraMon raster (.img + .rel) | Image | `.img`, `.rel` | read + write | missing | low | [#666](https://github.com/Hawkynt/PNGCrushCS/issues/666) |
| MRF (Meta Raster Format, NASA/Esri) | Image | `.mrf`, `.idx`, `.ptf`, `.pjg`, `.ppg`, `.pzp` | read + write | missing | low | [#667](https://github.com/Hawkynt/PNGCrushCS/issues/667) |
| NADCON .los/.las grid shift | Image | `.los`, `.las` | read + write | missing | low | [#668](https://github.com/Hawkynt/PNGCrushCS/issues/668) |
| NLAPS Data Format (NDF) | Image | `.h1`, `.h2`, `.hd` | read + write | missing | low | [#669](https://github.com/Hawkynt/PNGCrushCS/issues/669) |
| NOAA GEOCON/NADCON5 .b grid | Image | `.b` | read + write | missing | low | [#670](https://github.com/Hawkynt/PNGCrushCS/issues/670) |
| NOAA Polar Orbiter Level 1b (AVHRR L1B) | Image | `.l1b` | read + write | missing | low | [#671](https://github.com/Hawkynt/PNGCrushCS/issues/671) |
| NOAA vertical datum grid shift (GTX) | Image | `.gtx` | read + write | missing | low | [#672](https://github.com/Hawkynt/PNGCrushCS/issues/672) |
| Northwood / Vertical Mapper grid (NWT_GRD / NWT_GRC) | Image | `.grd`, `.grc` | read + write | missing | low | [#673](https://github.com/Hawkynt/PNGCrushCS/issues/673) |
| NSIDC sea-ice concentration binary | Image | `.bin` | read + write | missing | low | [#674](https://github.com/Hawkynt/PNGCrushCS/issues/674) |
| NTv2 datum grid shift | Image | `.gsb` | read + write | missing | low | [#675](https://github.com/Hawkynt/PNGCrushCS/issues/675) |
| PCI .aux labelled raw (PAux) | Image | `.aux`, `.raw` | read + write | missing | low | [#676](https://github.com/Hawkynt/PNGCrushCS/issues/676) |
| PCIDSK database file (PCI Geomatics .pix) | Image | `.pix` | read + write | missing | low | [#677](https://github.com/Hawkynt/PNGCrushCS/issues/677) |
| PCRaster raster (CSF) | Image | `.map` | read + write | missing | low | [#678](https://github.com/Hawkynt/PNGCrushCS/issues/678) |
| R Raster (RRASTER .grd/.gri) | Image | `.grd`, `.gri` | read + write | missing | low | [#679](https://github.com/Hawkynt/PNGCrushCS/issues/679) |
| RIK (Swedish Grid Maps) | Image | `.rik` | read + write | missing | low | [#680](https://github.com/Hawkynt/PNGCrushCS/issues/680) |
| RMF (Raster Matrix Format, Panorama GIS) | Image | `.rsw`, `.mtw` | read + write | missing | low | [#681](https://github.com/Hawkynt/PNGCrushCS/issues/681) |
| ROI_PAC raster | Image | `.int`, `.slc`, `.amp`, `.cor`, `.hgt`, `.unw` | read + write | missing | low | [#682](https://github.com/Hawkynt/PNGCrushCS/issues/682) |
| RPF: CADRG / CIB frame files and A.TOC | Image | `.toc`, `.ntf` | read + write | missing | low | [#683](https://github.com/Hawkynt/PNGCrushCS/issues/683) |
| SAGA GIS binary grid (SDAT/SGRD) | Image | `.sdat`, `.sgrd` | read + write | missing | low | [#684](https://github.com/Hawkynt/PNGCrushCS/issues/684) |
| Sandia GSAT File Format (GFF) | Image | `.gff` | read + write | missing | low | [#685](https://github.com/Hawkynt/PNGCrushCS/issues/685) |
| SDTS raster (DEM profile) | Image | `.ddf` | read + write | missing | low | [#686](https://github.com/Hawkynt/PNGCrushCS/issues/686) |
| SIGDEM (Scaled Integer Gridded DEM) | Image | `.sigdem` | read + write | missing | low | [#687](https://github.com/Hawkynt/PNGCrushCS/issues/687) |
| SNODAS | Image | `.hdr`, `.dat` | read + write | missing | low | [#688](https://github.com/Hawkynt/PNGCrushCS/issues/688) |
| SRP (ASRP / USRP Standard Raster Product) | Image | `.gen`, `.img`, `.thf` | read + write | missing | low | [#689](https://github.com/Hawkynt/PNGCrushCS/issues/689) |
| Terragen terrain (TER) | Image | `.ter` | read + write | missing | low | [#690](https://github.com/Hawkynt/PNGCrushCS/issues/690) |
| TerraSAR-X COSAR | Image | `.cos` | read + write | missing | low | [#691](https://github.com/Hawkynt/PNGCrushCS/issues/691) |
| USGS ASCII DEM (and CDED) | Image | `.dem` | read + write | missing | low | [#692](https://github.com/Hawkynt/PNGCrushCS/issues/692) |
| USGS DOQ (first generation) | Image | `.doq` | read + write | missing | low | [#693](https://github.com/Hawkynt/PNGCrushCS/issues/693) |
| USGS DOQ (labelled, second generation) | Image | `.doq` | read + write | missing | low | [#694](https://github.com/Hawkynt/PNGCrushCS/issues/694) |
| USGS ISIS2 cube | Image | `.cub` | read + write | missing | low | [#695](https://github.com/Hawkynt/PNGCrushCS/issues/695) |
| USGS LULC Composite Theme Grid (CTG) | Image | `grid_cell` | read + write | missing | low | [#696](https://github.com/Hawkynt/PNGCrushCS/issues/696) |
| Vaisala IRIS weather radar product | Image | `.raw` | read + write | missing | low | [#697](https://github.com/Hawkynt/PNGCrushCS/issues/697) |
| Vexcel MFF raster | Image | `.hdr`, `.b0`, `.r0`, `.i0` | read + write | missing | low | [#698](https://github.com/Hawkynt/PNGCrushCS/issues/698) |
| Vexcel MFF2 (HKV) raster | Image | `attrib`, `image_data` | read + write | missing | low | [#699](https://github.com/Hawkynt/PNGCrushCS/issues/699) |
| VTP Binary Terrain (BT) | Image | `.bt` | read + write | missing | low | [#700](https://github.com/Hawkynt/PNGCrushCS/issues/700) |
| ZMap Plus Grid | Image | `.dat`, `.zmap` | read + write | missing | low | [#701](https://github.com/Hawkynt/PNGCrushCS/issues/701) |

## Medical & scientific imaging

Medical volumes, microscopy, whole-slide, astronomy and lab instruments. Tracked in [#516](https://github.com/Hawkynt/PNGCrushCS/issues/516).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| DICOM compressed transfer syntaxes and full DICOM image model | Image | `.dcm`, `.dicom` | read + write | missing | high | [#702](https://github.com/Hawkynt/PNGCrushCS/issues/702) |
| Aperio SVS whole-slide image | Image | `.svs` | read + write | missing | medium | [#703](https://github.com/Hawkynt/PNGCrushCS/issues/703) |
| FITS tiled image compression (fpack: RICE_1, GZIP_1/2, HCOMPRESS_1, PLIO_1) | Image | `.fits`, `.fz` | read + write | missing | medium | [#704](https://github.com/Hawkynt/PNGCrushCS/issues/704) |
| FreeSurfer MGH / MGZ | Image | `.mgh`, `.mgz` | read + write | missing | medium | [#705](https://github.com/Hawkynt/PNGCrushCS/issues/705) |
| Gatan Digital Micrograph DM3 / DM4 | Image | `.dm3`, `.dm4` | read + write | missing | medium | [#706](https://github.com/Hawkynt/PNGCrushCS/issues/706) |
| Leica LIF (Leica Image File) | Image | `.lif`, `.lof`, `.xlef` | read + write | missing | medium | [#707](https://github.com/Hawkynt/PNGCrushCS/issues/707) |
| MINC 1 / MINC 2 (Medical Imaging NetCDF) | Image | `.mnc`, `.mnc2` | read + write | missing | medium | [#708](https://github.com/Hawkynt/PNGCrushCS/issues/708) |
| Nikon ND2 (NIS-Elements) | Image | `.nd2` | read + write | missing | medium | [#709](https://github.com/Hawkynt/PNGCrushCS/issues/709) |
| XISF (Extensible Image Serialization Format) | Image | `.xisf` | read + write | missing | medium | [#710](https://github.com/Hawkynt/PNGCrushCS/issues/710) |
| Zeiss CZI | Image | `.czi` | read + write | missing | medium | [#711](https://github.com/Hawkynt/PNGCrushCS/issues/711) |
| 3DHISTECH MIRAX (MRXS) | Image | `.mrxs` | read + write | missing | low | [#712](https://github.com/Hawkynt/PNGCrushCS/issues/712) |
| AFNI BRIK/HEAD | Image | `.BRIK`, `.HEAD`, `.BRIK.gz` | read + write | missing | low | [#713](https://github.com/Hawkynt/PNGCrushCS/issues/713) |
| Amira/Avizo AmiraMesh lattice (AM) | Image | `.am` | read + write | missing | low | [#714](https://github.com/Hawkynt/PNGCrushCS/issues/714) |
| Andor SIF (Solis) | Image | `.sif` | read + write | missing | low | [#715](https://github.com/Hawkynt/PNGCrushCS/issues/715) |
| ASTM E57 (embedded 2D images) | Image | `.e57` | read + write | missing | low | [#716](https://github.com/Hawkynt/PNGCrushCS/issues/716) |
| DeltaVision (DV / R3D) | Image | `.dv`, `.r3d`, `.r3d_d3d` | read + write | missing | low | [#717](https://github.com/Hawkynt/PNGCrushCS/issues/717) |
| ECAT 6 / ECAT 7 (Siemens/CTI PET) | Image | `.v`, `.img`, `.i` | read + write | missing | low | [#718](https://github.com/Hawkynt/PNGCrushCS/issues/718) |
| FEI/Thermo TIA SER/EMI | Image | `.ser`, `.emi` | read + write | missing | low | [#719](https://github.com/Hawkynt/PNGCrushCS/issues/719) |
| GIPL (Guys Image Processing Lab) | Image | `.gipl`, `.gipl.gz` | read + write | missing | low | [#720](https://github.com/Hawkynt/PNGCrushCS/issues/720) |
| Gould imaging system file | Image |  | read + write | missing | low | [#721](https://github.com/Hawkynt/PNGCrushCS/issues/721) |
| Hamamatsu NDPI / VMS / VMU | Image | `.ndpi`, `.vms`, `.vmu` | read + write | missing | low | [#722](https://github.com/Hawkynt/PNGCrushCS/issues/722) |
| HIPS (Human Information Processing Lab image) | Image |  | read + write | missing | low | [#723](https://github.com/Hawkynt/PNGCrushCS/issues/723) |
| IMAGIC (cryo-EM image stack) | Image | `.hed`, `.img` | read + write | missing | low | [#724](https://github.com/Hawkynt/PNGCrushCS/issues/724) |
| Imaris IMS (HDF5) | Image | `.ims` | read + write | missing | low | [#725](https://github.com/Hawkynt/PNGCrushCS/issues/725) |
| ImgStar (IMG*) high-precision formats | Image | `.flt`, `.cpx` | read + write | missing | low | [#726](https://github.com/Hawkynt/PNGCrushCS/issues/726) |
| KONTRON image | Image | `.img` | read + write | missing | low | [#727](https://github.com/Hawkynt/PNGCrushCS/issues/727) |
| LabEye / IFUNC IM (Pillow 'IM') | Image | `.im` | read + write | missing | low | [#728](https://github.com/Hawkynt/PNGCrushCS/issues/728) |
| LIFF (Openlab Layered Image File Format) | Image | `.liff` | read + write | missing | low | [#729](https://github.com/Hawkynt/PNGCrushCS/issues/729) |
| MetaMorph STK (TIFF with UIC tags) | Image | `.stk`, `.nd` | read + write | missing | low | [#730](https://github.com/Hawkynt/PNGCrushCS/issues/730) |
| MRtrix image (MIF/MIH) | Image | `.mif`, `.mih`, `.mif.gz` | read + write | missing | low | [#731](https://github.com/Hawkynt/PNGCrushCS/issues/731) |
| NASA Raster Metafile | Image |  | read + write | missing | low | [#732](https://github.com/Hawkynt/PNGCrushCS/issues/732) |
| Olympus FluoView OIB / OIF | Image | `.oib`, `.oif` | read + write | missing | low | [#733](https://github.com/Hawkynt/PNGCrushCS/issues/733) |
| Olympus OIR (FluoView FV3000/cellSens) | Image | `.oir` | read + write | missing | low | [#734](https://github.com/Hawkynt/PNGCrushCS/issues/734) |
| Olympus/Evident VSI (cellSens slide) | Image | `.vsi`, `.ets` | read + write | missing | low | [#735](https://github.com/Hawkynt/PNGCrushCS/issues/735) |
| Philips PAR/REC | Image | `.par`, `.rec`, `.PAR`, `.REC` | read + write | missing | low | [#736](https://github.com/Hawkynt/PNGCrushCS/issues/736) |
| Princeton Instruments SPE (WinView/LightField) | Image | `.spe` | read + write | missing | low | [#737](https://github.com/Hawkynt/PNGCrushCS/issues/737) |
| SPIDER image/volume | Image | `.spi`, `.spider`, `.dat` | read + write | missing | low | [#738](https://github.com/Hawkynt/PNGCrushCS/issues/738) |
| TkImg RAW | Image | `.raw` | read + write | missing | low | [#739](https://github.com/Hawkynt/PNGCrushCS/issues/739) |
| Vista data file (Vfile/Vplain) | Image | `.v`, `.vp` | read + write | missing | low | [#740](https://github.com/Hawkynt/PNGCrushCS/issues/740) |
| VTK image data (legacy STRUCTURED_POINTS .vtk and XML .vti) | Image | `.vtk`, `.vti` | read + write | missing | low | [#741](https://github.com/Hawkynt/PNGCrushCS/issues/741) |
| WinMiPS PIC | Image | `.pic` | read + write | missing | low | [#742](https://github.com/Hawkynt/PNGCrushCS/issues/742) |
| Zeiss ZVI (AxioVision) | Image | `.zvi` | read + write | missing | low | [#743](https://github.com/Hawkynt/PNGCrushCS/issues/743) |

## GPU textures & game assets

GPU/console texture containers, game sprites and engine assets. Tracked in [#517](https://github.com/Hawkynt/PNGCrushCS/issues/517).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Basis Universal (.basis) | Image | `.basis` | read + write | missing | high | [#744](https://github.com/Hawkynt/PNGCrushCS/issues/744) |
| KTX2 supercompression (BasisLZ, Zstandard, ZLIB) and UASTC payloads | Image | `.ktx2` | read + write | missing | high | [#745](https://github.com/Hawkynt/PNGCrushCS/issues/745) |
| Crunch (.crn) | Image | `.crn` | read + write | missing | medium | [#746](https://github.com/Hawkynt/PNGCrushCS/issues/746) |
| Entis Rasterized Image (ERI) | Image | `.eri`, `.mei` | read + write | blocked | medium | [#747](https://github.com/Hawkynt/PNGCrushCS/issues/747) |
| Nintendo GameCube/Wii TPL | Image | `.tpl` | read + write | missing | medium | [#748](https://github.com/Hawkynt/PNGCrushCS/issues/748) |
| RenderWare Texture Dictionary (TXD) | Image | `.txd` | read + write | missing | medium | [#749](https://github.com/Hawkynt/PNGCrushCS/issues/749) |
| XNA / MonoGame XNB (Texture2D) | Image | `.xnb` | read + write | missing | medium | [#750](https://github.com/Hawkynt/PNGCrushCS/issues/750) |
| BRender PIX | Image | `.pix` | read + write | missing | low | [#751](https://github.com/Hawkynt/PNGCrushCS/issues/751) |
| Command Simulations ILBM | Image |  | read + write | missing | low | [#752](https://github.com/Hawkynt/PNGCrushCS/issues/752) |
| Diablo / Hellfire CEL and CL2 | Image | `.cel`, `.cl2` | read + write | missing | low | [#753](https://github.com/Hawkynt/PNGCrushCS/issues/753) |
| Doom picture (patch) format and WAD lumps | Image | `.lmp`, `.wad` | read + write | missing | low | [#754](https://github.com/Hawkynt/PNGCrushCS/issues/754) |
| EA SSH (PS2 'SHPS' shape archive) | Image | `.ssh` | read + write | missing | low | [#755](https://github.com/Hawkynt/PNGCrushCS/issues/755) |
| Fallout FRM | Image | `.frm`, `.fr0` | read + write | missing | low | [#756](https://github.com/Hawkynt/PNGCrushCS/issues/756) |
| Godot compressed texture (.ctex / .stex) | Image | `.ctex`, `.stex` | read + write | missing | low | [#757](https://github.com/Hawkynt/PNGCrushCS/issues/757) |
| Half-Life sprite (IDSP version 2) | Image | `.spr` | read + write | missing | low | [#758](https://github.com/Hawkynt/PNGCrushCS/issues/758) |
| Heretic II / SiN M32 texture | Image | `.m32` | read + write | missing | low | [#759](https://github.com/Hawkynt/PNGCrushCS/issues/759) |
| LithTech DTX texture | Image | `.dtx` | read + write | missing | low | [#760](https://github.com/Hawkynt/PNGCrushCS/issues/760) |
| Nintendo 3DS BCLIM / Wii U BFLIM | Image | `.bclim`, `.bflim` | read + write | missing | low | [#761](https://github.com/Hawkynt/PNGCrushCS/issues/761) |
| Nintendo 3DS CTPK texture package | Image | `.ctpk` | read + write | missing | low | [#762](https://github.com/Hawkynt/PNGCrushCS/issues/762) |
| Nintendo DS Nitro 2D graphics (NCGR/NCLR/NSCR/NCER) | Image | `.ncgr`, `.nclr`, `.nscr`, `.ncer`, `.nbgr` | read + write | missing | low | [#763](https://github.com/Hawkynt/PNGCrushCS/issues/763) |
| Nintendo DS NSBTX / BTX0 textures | Image | `.nsbtx`, `.btx` | read + write | missing | low | [#764](https://github.com/Hawkynt/PNGCrushCS/issues/764) |
| Nintendo GameCube BTI | Image | `.bti` | read + write | missing | low | [#765](https://github.com/Hawkynt/PNGCrushCS/issues/765) |
| Nintendo Switch BNTX | Image | `.bntx` | read + write | missing | low | [#766](https://github.com/Hawkynt/PNGCrushCS/issues/766) |
| Nintendo Wii TEX0 / BRTEX (BRRES textures) | Image | `.tex0`, `.brtex` | read + write | missing | low | [#767](https://github.com/Hawkynt/PNGCrushCS/issues/767) |
| PlayStation 3 GTF texture | Image | `.gtf` | read + write | missing | low | [#768](https://github.com/Hawkynt/PNGCrushCS/issues/768) |
| PlayStation 4 GNF | Image | `.gnf` | read + write | missing | low | [#769](https://github.com/Hawkynt/PNGCrushCS/issues/769) |
| PlayStation Portable GIM | Image | `.gim` | read + write | missing | low | [#770](https://github.com/Hawkynt/PNGCrushCS/issues/770) |
| PlayStation Vita GXT | Image | `.gxt` | read + write | missing | low | [#771](https://github.com/Hawkynt/PNGCrushCS/issues/771) |
| PowerVR legacy PVR (v1/v2 'PVR!' header) | Image | `.pvr` | read + write | missing | low | [#772](https://github.com/Hawkynt/PNGCrushCS/issues/772) |
| Quake/Half-Life miptex lump (MIP) | Image | `.mip` | read + write | missing | low | [#773](https://github.com/Hawkynt/PNGCrushCS/issues/773) |
| Sega Dreamcast PVR (PVRT/GBIX) | Image | `.pvr` | read + write | missing | low | [#774](https://github.com/Hawkynt/PNGCrushCS/issues/774) |
| Sega GVR (GameCube PVR variant) | Image | `.gvr` | read + write | missing | low | [#775](https://github.com/Hawkynt/PNGCrushCS/issues/775) |
| Serious Engine texture (Croteam .tex) | Image | `.tex` | read + write | blocked | low | [#776](https://github.com/Hawkynt/PNGCrushCS/issues/776) |
| Source 2 compiled texture (vtex_c) | Image | `.vtex_c` | read + write | missing | low | [#777](https://github.com/Hawkynt/PNGCrushCS/issues/777) |
| StarCraft / Warcraft II GRP | Image | `.grp` | read + write | missing | low | [#778](https://github.com/Hawkynt/PNGCrushCS/issues/778) |
| Unreal Engine 1/2 texture package (UTX) | Image | `.utx` | read + write | missing | low | [#779](https://github.com/Hawkynt/PNGCrushCS/issues/779) |
| V.Flash PTX | Image | `.ptx` | read + write | missing | low | [#780](https://github.com/Hawkynt/PNGCrushCS/issues/780) |
| Westwood SHP (Command & Conquer / Red Alert / TS) | Image | `.shp` | read + write | missing | low | [#781](https://github.com/Hawkynt/PNGCrushCS/issues/781) |
| Wii U GTX / GFD texture | Image | `.gtx` | read + write | missing | low | [#782](https://github.com/Hawkynt/PNGCrushCS/issues/782) |
| Xbox / Xbox 360 XPR resource (XPR0 / XPR2) | Image | `.xpr` | read + write | missing | low | [#783](https://github.com/Hawkynt/PNGCrushCS/issues/783) |

## Retro computer & legacy platforms

8/16-bit computer screens, workstations, calculators, handhelds. Tracked in [#518](https://github.com/Hawkynt/PNGCrushCS/issues/518).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Yanagisawa PIC (X68000, PC-88VA, FM Towns, MSX YPIC) | Image | `.pic` | read + write | missing | medium | [#784](https://github.com/Hawkynt/PNGCrushCS/issues/784) |
| Alpha Microsystems BMP | Image | `.bmp` | read + write | missing | low | [#785](https://github.com/Hawkynt/PNGCrushCS/issues/785) |
| AMI BIOS logo (GRFX) | Image | `.grfx`, `.grf` | read + write | missing | low | [#786](https://github.com/Hawkynt/PNGCrushCS/issues/786) |
| AMOS Picture Packer (stand-alone .bin) | Image | `.bin` | read + write | missing | low | [#787](https://github.com/Hawkynt/PNGCrushCS/issues/787) |
| AniST (Atari ST animation) | Image | `.scr`, `.str` | read + write | missing | low | [#788](https://github.com/Hawkynt/PNGCrushCS/issues/788) |
| Apple volume label image | Image |  | read + write | missing | low | [#789](https://github.com/Hawkynt/PNGCrushCS/issues/789) |
| Arabesque bitmap | Image | `.abm`, `.puf` | read + write | missing | low | [#790](https://github.com/Hawkynt/PNGCrushCS/issues/790) |
| Atari 8-bit ART variants (Artist by David Eaton, Ascii-Art Editor, mono ART) | Image | `.art` | read + write | missing | low | [#791](https://github.com/Hawkynt/PNGCrushCS/issues/791) |
| Atari 8-bit KFX | Image | `.kfx` | read + write | missing | low | [#792](https://github.com/Hawkynt/PNGCrushCS/issues/792) |
| Atari 8-bit PIC variants (Graphic Arts Department, PIC v2, Visualizer) | Image | `.pic` | read + write | missing | low | [#793](https://github.com/Hawkynt/PNGCrushCS/issues/793) |
| Atari ST GFA Artist and Palette Master (.art) | Image | `.art` | read + write | missing | low | [#794](https://github.com/Hawkynt/PNGCrushCS/issues/794) |
| Atari ST RGB Intermediate format | Image | `.rgb` | read + write | missing | low | [#795](https://github.com/Hawkynt/PNGCrushCS/issues/795) |
| Borland BGI image and RIPscrip icon (ICN, BGI) | Image | `.bgi`, `.icn`, `.msk`, `.hot` | read + write | missing | low | [#796](https://github.com/Hawkynt/PNGCrushCS/issues/796) |
| Canvas compressed (Atari ST CPT) with HBL palettes | Image | `.cpt`, `.hbl` | read + write | missing | low | [#797](https://github.com/Hawkynt/PNGCrushCS/issues/797) |
| CD-i IFF IMAG | Image |  | read + write | missing | low | [#798](https://github.com/Hawkynt/PNGCrushCS/issues/798) |
| Clear (RISC OS Translator) | Image |  | read + write | missing | low | [#799](https://github.com/Hawkynt/PNGCrushCS/issues/799) |
| ColorViewSquash (Atari 8-bit RGB) | Image | `.rgb` | read + write | missing | low | [#800](https://github.com/Hawkynt/PNGCrushCS/issues/800) |
| Commodore 64 VIC (.vic) | Image | `.vic` | read + write | missing | low | [#801](https://github.com/Hawkynt/PNGCrushCS/issues/801) |
| Continuous Edge Graphic (Edsun CEG) | Image | `.ceg` | read + write | missing | low | [#802](https://github.com/Hawkynt/PNGCrushCS/issues/802) |
| DataShow graphic, screen and sprite | Image | `.gra`, `.scr`, `.spr` | read + write | missing | low | [#803](https://github.com/Hawkynt/PNGCrushCS/issues/803) |
| DaVinci (NEC PC-88 .img) | Image | `.img` | read + write | missing | low | [#804](https://github.com/Hawkynt/PNGCrushCS/issues/804) |
| DeskMate Paint (PNT) | Image | `.pnt` | read + write | missing | low | [#805](https://github.com/Hawkynt/PNGCrushCS/issues/805) |
| Draw256 VGA picture | Image | `.vga` | read + write | missing | low | [#806](https://github.com/Hawkynt/PNGCrushCS/issues/806) |
| DXP image | Image | `.dxp` | read + write | missing | low | [#807](https://github.com/Hawkynt/PNGCrushCS/issues/807) |
| Dynamic Publisher (MSX2) font and screen | Image | `.fnt`, `.pct` | read + write | missing | low | [#808](https://github.com/Hawkynt/PNGCrushCS/issues/808) |
| eLeMeNt ZX graphics (HGFX Low/High/HAM/Ultra, HiResColor, HiResindeXedColor, KeyLayer) | Image | `.hgl`, `.hgh`, `.ham`, `.shr`, `.hgs`, `.hkl` | read + write | missing | low | [#809](https://github.com/Hawkynt/PNGCrushCS/issues/809) |
| Envision / EnvisionPC (Atari 8-bit .map) | Image | `.map` | read + write | missing | low | [#810](https://github.com/Hawkynt/PNGCrushCS/issues/810) |
| EPOC/Psion Sketch | Image |  | read + write | missing | low | [#811](https://github.com/Hawkynt/PNGCrushCS/issues/811) |
| Flogiciels image (CPT, LCR) | Image | `.cpt`, `.lcr` | read + write | missing | low | [#812](https://github.com/Hawkynt/PNGCrushCS/issues/812) |
| FM Towns HEL animation | Image | `.hel` | read + write | missing | low | [#813](https://github.com/Hawkynt/PNGCrushCS/issues/813) |
| Funny Paint (Atari Falcon .fun) | Image | `.fun` | read + write | missing | low | [#814](https://github.com/Hawkynt/PNGCrushCS/issues/814) |
| Garmin vehicle image (SRF) | Image | `.srf` | read + write | missing | low | [#815](https://github.com/Hawkynt/PNGCrushCS/issues/815) |
| GEM-View Dither (.dit) | Image | `.dit` | read + write | missing | low | [#816](https://github.com/Hawkynt/PNGCrushCS/issues/816) |
| Genus Graphics Library GX1/GX2 | Image | `.gx1`, `.gx2` | read + write | missing | low | [#817](https://github.com/Hawkynt/PNGCrushCS/issues/817) |
| GeoWorks Ensemble background (.000/.geo) | Image | `.000`, `.geo` | read + write | missing | low | [#818](https://github.com/Hawkynt/PNGCrushCS/issues/818) |
| GLPaint PIC | Image | `.pic` | read + write | missing | low | [#819](https://github.com/Hawkynt/PNGCrushCS/issues/819) |
| GrayPaint (Palm OS) | Image | `.pdb` | read + write | missing | low | [#820](https://github.com/Hawkynt/PNGCrushCS/issues/820) |
| HP Starbase bitmap file | Image |  | read + write | missing | low | [#821](https://github.com/Hawkynt/PNGCrushCS/issues/821) |
| HSI Raw / HSI JPEG (Handmade Software Image Alchemy) | Image | `.raw`, `.hst`, `.jpg` | read + write | missing | low | [#822](https://github.com/Hawkynt/PNGCrushCS/issues/822) |
| HTC splash screen (NB) | Image | `.nb`, `.img` | read + write | missing | low | [#823](https://github.com/Hawkynt/PNGCrushCS/issues/823) |
| IBM AVC Still Video Image | Image | `._im`, `.!im`, `.id` | read + write | missing | low | [#824](https://github.com/Hawkynt/PNGCrushCS/issues/824) |
| IBM Storyboard PIC/CAP | Image | `.pic`, `.cap` | read + write | missing | low | [#825](https://github.com/Hawkynt/PNGCrushCS/issues/825) |
| IFF RGFX (Retargetable Graphic) | Image | `.rgfx`, `.rgx` | read + write | missing | low | [#826](https://github.com/Hawkynt/PNGCrushCS/issues/826) |
| ImageWise picture (.iw) | Image | `.iw` | read + write | missing | low | [#827](https://github.com/Hawkynt/PNGCrushCS/issues/827) |
| Img Software Set (Paul Raveling) multi-file images | Image | `.p`, `.a`, `.r`, `.g`, `.b` | read + write | missing | low | [#828](https://github.com/Hawkynt/PNGCrushCS/issues/828) |
| Img-whatnot (Ed Falk's 'img' format) | Image | `.img` | read + write | missing | low | [#829](https://github.com/Hawkynt/PNGCrushCS/issues/829) |
| Inset PIX | Image | `.pix` | read + write | missing | low | [#830](https://github.com/Hawkynt/PNGCrushCS/issues/830) |
| IPI (The Complete Image) | Image | `.ipi` | read + write | missing | low | [#831](https://github.com/Hawkynt/PNGCrushCS/issues/831) |
| JAM compressed image (DOS) | Image | `.jam` | read + write | missing | low | [#832](https://github.com/Hawkynt/PNGCrushCS/issues/832) |
| Jeff's Image Format (JIF) | Image | `.jif` | read + write | missing | low | [#833](https://github.com/Hawkynt/PNGCrushCS/issues/833) |
| Jing Tao pic (SSL) | Image | `.ssl` | read + write | missing | low | [#834](https://github.com/Hawkynt/PNGCrushCS/issues/834) |
| KYG (Kyss) | Image | `.kyg` | read + write | missing | low | [#835](https://github.com/Hawkynt/PNGCrushCS/issues/835) |
| Lisp Machine bitmap | Image | `.lispm` | read + write | missing | low | [#836](https://github.com/Hawkynt/PNGCrushCS/issues/836) |
| Lotus Manuscript graphics (.bit, .rle) | Image | `.bit`, `.rle` | read + write | missing | low | [#837](https://github.com/Hawkynt/PNGCrushCS/issues/837) |
| Lumena CEL and PIX/BPX | Image | `.cel`, `.pix`, `.bpx` | read + write | missing | low | [#838](https://github.com/Hawkynt/PNGCrushCS/issues/838) |
| MagicDraw (TRS-80 Model 4 .shr) | Image | `.shr` | read + write | missing | low | [#839](https://github.com/Hawkynt/PNGCrushCS/issues/839) |
| Marco (Macro) Pixel Editor (Atari 8-bit CPI) | Image | `.cpi` | read + write | missing | low | [#840](https://github.com/Hawkynt/PNGCrushCS/issues/840) |
| MIF (MAXpaint) | Image | `.mif` | read + write | missing | low | [#841](https://github.com/Hawkynt/PNGCrushCS/issues/841) |
| MIF (MSX Interchange Format) | Image | `.mif` | read + write | missing | low | [#842](https://github.com/Hawkynt/PNGCrushCS/issues/842) |
| MS Word for DOS screen capture (SCR/MWG) | Image | `.scr`, `.mwg` | read + write | missing | low | [#843](https://github.com/Hawkynt/PNGCrushCS/issues/843) |
| Multi Color Map (Atari 8-bit MCM) | Image | `.mcm` | read + write | missing | low | [#844](https://github.com/Hawkynt/PNGCrushCS/issues/844) |
| NCSA Telnet Interactive Color Raster (ICR) | Image | `.icr` | read + write | missing | low | [#845](https://github.com/Hawkynt/PNGCrushCS/issues/845) |
| NETPIC (Jim Tucker) | Image | `.npx`, `.com` | read + write | missing | low | [#846](https://github.com/Hawkynt/PNGCrushCS/issues/846) |
| NIFF (xloadimage native format) | Image | `.niff` | read + write | missing | low | [#847](https://github.com/Hawkynt/PNGCrushCS/issues/847) |
| Nokia Startup Logo (NSL) | Image | `.nsl` | read + write | missing | low | [#848](https://github.com/Hawkynt/PNGCrushCS/issues/848) |
| NPZ (Npic) | Image | `.npz` | read + write | missing | low | [#849](https://github.com/Hawkynt/PNGCrushCS/issues/849) |
| OS/2 boot logo (LGO) | Image | `.lgo`, `.bgo` | read + write | missing | low | [#850](https://github.com/Hawkynt/PNGCrushCS/issues/850) |
| Palm Diddle / DiddleBug sketch | Image | `.pdb` | read + write | missing | low | [#851](https://github.com/Hawkynt/PNGCrushCS/issues/851) |
| PCR image | Image | `.pcr` | read + write | missing | low | [#852](https://github.com/Hawkynt/PNGCrushCS/issues/852) |
| PIC Editor sprite library (Andrei Romanov) | Image | `.pic` | read + write | missing | low | [#853](https://github.com/Hawkynt/PNGCrushCS/issues/853) |
| PIF (Portable Image Format) | Image | `.pif` | read + write | missing | low | [#854](https://github.com/Hawkynt/PNGCrushCS/issues/854) |
| PixArt (Atari Falcon .pix) | Image | `.pix` | read + write | missing | low | [#855](https://github.com/Hawkynt/PNGCrushCS/issues/855) |
| Pixel Studio (FileStream) IBW | Image | `.ibw` | read + write | missing | low | [#856](https://github.com/Hawkynt/PNGCrushCS/issues/856) |
| PixelPaint (SuperMac) | Image |  | read + write | missing | low | [#857](https://github.com/Hawkynt/PNGCrushCS/issues/857) |
| PKI (ImgFun) | Image | `.pki` | read + write | missing | low | [#858](https://github.com/Hawkynt/PNGCrushCS/issues/858) |
| QDV (Random Dot Software) | Image | `.qdv` | read + write | missing | low | [#859](https://github.com/Hawkynt/PNGCrushCS/issues/859) |
| RastaConverter output (Atari 8-bit MIC + PMG + RP) | Image | `.mic`, `.pmg`, `.rp` | read + write | missing | low | [#860](https://github.com/Hawkynt/PNGCrushCS/issues/860) |
| Raster Master RAW | Image | `.raw` | read + write | missing | low | [#861](https://github.com/Hawkynt/PNGCrushCS/issues/861) |
| RIFF DIB (RDIB) | Image | `.rdi`, `.rdib` | read + write | missing | low | [#862](https://github.com/Hawkynt/PNGCrushCS/issues/862) |
| Signum! compressed image (IMC) and Signum bitmap (PAC) | Image | `.imc` | read + write | missing | low | [#863](https://github.com/Hawkynt/PNGCrushCS/issues/863) |
| Sinclair QL screen dump | Image | `.scr` | read + write | missing | low | [#864](https://github.com/Hawkynt/PNGCrushCS/issues/864) |
| Sony PictureGear Pocket (PRC) | Image | `.prc` | read + write | blocked | low | [#865](https://github.com/Hawkynt/PNGCrushCS/issues/865) |
| Spectrum 512 Anispec (Atari ST animation) | Image | `.sps` | read + write | missing | low | [#866](https://github.com/Hawkynt/PNGCrushCS/issues/866) |
| SprEd sprite (Atari 8-bit .spr) | Image | `.spr` | read + write | missing | low | [#867](https://github.com/Hawkynt/PNGCrushCS/issues/867) |
| Sprite Master (SPR + ARY) | Image | `.spr`, `.ary` | read + write | missing | low | [#868](https://github.com/Hawkynt/PNGCrushCS/issues/868) |
| STOS memory banks and Picture Packer (Atari ST) | Image | `.mbk`, `.mbs`, `.pp1`, `.pp2`, `.pp3`, `.daj` | read + write | missing | low | [#869](https://github.com/Hawkynt/PNGCrushCS/issues/869) |
| Tandyvision image (.vga) | Image | `.vga` | read + write | missing | low | [#870](https://github.com/Hawkynt/PNGCrushCS/issues/870) |
| TealPaint (Palm OS) | Image | `.pdb` | read + write | missing | low | [#871](https://github.com/Hawkynt/PNGCrushCS/issues/871) |
| TelePaint / Splash graphics (.ss, .st) | Image | `.ss`, `.st` | read + write | missing | low | [#872](https://github.com/Hawkynt/PNGCrushCS/issues/872) |
| TI-92 / TI-89 picture variable (92i) | Image | `.92i` | read + write | blocked | low | [#873](https://github.com/Hawkynt/PNGCrushCS/issues/873) |
| Title-Maker self-displaying EXE | Image | `.exe` | read + write | missing | low | [#874](https://github.com/Hawkynt/PNGCrushCS/issues/874) |
| True Colour Sprites (Atari Falcon .trs) | Image | `.trs` | read + write | missing | low | [#875](https://github.com/Hawkynt/PNGCrushCS/issues/875) |
| VEGX | Image | `.vegx` | read + write | missing | low | [#876](https://github.com/Hawkynt/PNGCrushCS/issues/876) |
| VGAPaint 386 animation (VAN) and self-displaying COM/PCX programs | Image | `.van`, `.com`, `.exe` | read + write | missing | low | [#877](https://github.com/Hawkynt/PNGCrushCS/issues/877) |
| WGT Block and Sprite files | Image |  | read + write | missing | low | [#878](https://github.com/Hawkynt/PNGCrushCS/issues/878) |
| Windows device-dependent bitmap (DDB) | Image |  | read + write | missing | low | [#879](https://github.com/Hawkynt/PNGCrushCS/issues/879) |
| WOBA (HyperCard card/background bitmap) | Image |  | read + write | missing | low | [#880](https://github.com/Hawkynt/PNGCrushCS/issues/880) |
| Xerox Doodle brush | Image | `.brush` | read + write | missing | low | [#881](https://github.com/Hawkynt/PNGCrushCS/issues/881) |
| Young Picasso (YP) | Image | `.yp` | read + write | missing | low | [#882](https://github.com/Hawkynt/PNGCrushCS/issues/882) |
| ZX Spectrum ULAplus Timex hi-colour (TCU) | Image | `.tcu` | read + write | missing | low | [#883](https://github.com/Hawkynt/PNGCrushCS/issues/883) |
| ZX-Uno Radastan mode (.rad) | Image | `.rad` | read + write | missing | low | [#884](https://github.com/Hawkynt/PNGCrushCS/issues/884) |
| ZXPaintyONE v2.0 RAW (ZX81) | Image | `.raw` | read + write | missing | low | [#885](https://github.com/Hawkynt/PNGCrushCS/issues/885) |

## Vector, document & print

Vector drawings, metafiles, page description, printer rasters, fax. Tracked in [#519](https://github.com/Hawkynt/PNGCrushCS/issues/519).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| CUPS Raster (v1/v2/v3) | Image | `.ras`, `.cups` | read + write | missing | medium | [#886](https://github.com/Hawkynt/PNGCrushCS/issues/886) |
| EMF+ (EMF Plus) records | Image | `.emf`, `.emz` | read + write | missing | medium | [#887](https://github.com/Hawkynt/PNGCrushCS/issues/887) |
| Lottie animation (Bodymovin JSON, dotLottie) | Image | `.json`, `.lottie` | read + write | missing | medium | [#888](https://github.com/Hawkynt/PNGCrushCS/issues/888) |
| Microsoft Visio drawing (VSD, VSDX, VSS, VSSX, VSDM, VDX) | Image | `.vsd`, `.vsdx`, `.vsdm`, `.vss`, `.vssx`, `.vdx` | read + write | missing | medium | [#889](https://github.com/Hawkynt/PNGCrushCS/issues/889) |
| OpenDocument Drawing / StarOffice XML Draw (ODG, OTG, FODG, SXD) | Image | `.odg`, `.otg`, `.fodg`, `.sxd`, `.std` | read + write | missing | medium | [#890](https://github.com/Hawkynt/PNGCrushCS/issues/890) |
| PDF page rendering (content streams) | Image | `.pdf`, `.ai` | read + write | blocked | medium | [#891](https://github.com/Hawkynt/PNGCrushCS/issues/891) |
| PWG Raster (PWG 5102.4) | Image | `.pwg` | read + write | missing | medium | [#892](https://github.com/Hawkynt/PNGCrushCS/issues/892) |
| 101 Clips image | Image | `.img` | read + write | missing | low | [#893](https://github.com/Hawkynt/PNGCrushCS/issues/893) |
| Acorn ArtWorks | Image | `.aw` | read + write | missing | low | [#894](https://github.com/Hawkynt/PNGCrushCS/issues/894) |
| Acorn RISC OS Draw file | Image | `.aff` | read + write | missing | low | [#895](https://github.com/Hawkynt/PNGCrushCS/issues/895) |
| Adobe Flash SWF (vector stills and animation) | Image | `.swf` | read + write | missing | low | [#896](https://github.com/Hawkynt/PNGCrushCS/issues/896) |
| Affinity Photo / Designer / Publisher documents | Image | `.afphoto`, `.afdesign`, `.afpub` | read + write | missing | low | [#897](https://github.com/Hawkynt/PNGCrushCS/issues/897) |
| AmiDraw SDW | Image | `.sdw` | read + write | missing | low | [#898](https://github.com/Hawkynt/PNGCrushCS/issues/898) |
| Amiga Metafile (AMFF) | Image | `.amf` | read + write | missing | low | [#899](https://github.com/Hawkynt/PNGCrushCS/issues/899) |
| Apple Unirast (URF, AirPrint raster) | Image | `.urf` | read + write | missing | low | [#900](https://github.com/Hawkynt/PNGCrushCS/issues/900) |
| Applixware Bitmap (.im) | Image | `.im` | read + write | missing | low | [#901](https://github.com/Hawkynt/PNGCrushCS/issues/901) |
| Arts & Letters graphics and clip-art library (GED, YAL) | Image | `.ged`, `.yal` | read + write | missing | low | [#902](https://github.com/Hawkynt/PNGCrushCS/issues/902) |
| Async Professional Fax (APF) | Image | `.apf` | read + write | missing | low | [#903](https://github.com/Hawkynt/PNGCrushCS/issues/903) |
| AutoCAD Slide (SLD) and Slide Library (SLB) | Image | `.sld`, `.slb` | read + write | missing | low | [#904](https://github.com/Hawkynt/PNGCrushCS/issues/904) |
| Autodesk Design Web Format (DWF / DWFx) | Image | `.dwf`, `.dwfx` | read + write | missing | low | [#905](https://github.com/Hawkynt/PNGCrushCS/issues/905) |
| Calamus Vector Graphic (CVG) | Image | `.cvg` | read + write | missing | low | [#906](https://github.com/Hawkynt/PNGCrushCS/issues/906) |
| CGM character and clear-text encodings | Image | `.cgm` | read + write | blocked | low | [#907](https://github.com/Hawkynt/PNGCrushCS/issues/907) |
| Complete PC / Complete Fax (CPF, CFP) | Image | `.cpf`, `.cfp` | read + write | missing | low | [#908](https://github.com/Hawkynt/PNGCrushCS/issues/908) |
| Compressed Windows metafiles (WMZ / EMZ) | Image | `.wmz`, `.emz` | read + write | missing | low | [#909](https://github.com/Hawkynt/PNGCrushCS/issues/909) |
| Corel Compressed Exchange (CCX) | Image | `.ccx` | read + write | missing | low | [#910](https://github.com/Hawkynt/PNGCrushCS/issues/910) |
| Corel Designer, CorelChart, Corel R.A.V.E., CorelMOSAIC | Image | `.des`, `.cch`, `.clk`, `.clb`, `.clh` | read + write | missing | low | [#911](https://github.com/Hawkynt/PNGCrushCS/issues/911) |
| DCS - Desktop Color Separation (EPS) | Image | `.eps`, `.dcs`, `.c`, `.m`, `.y`, `.k` | read + write | missing | low | [#912](https://github.com/Hawkynt/PNGCrushCS/issues/912) |
| DEC DDIF (Digital Document Interchange Format) images | Image | `.ddif` | read + write | missing | low | [#913](https://github.com/Hawkynt/PNGCrushCS/issues/913) |
| Dia diagram | Image | `.dia` | read + write | missing | low | [#914](https://github.com/Hawkynt/PNGCrushCS/issues/914) |
| DjVu Color/Bitonal RLE (csepdjvu input) | Image | `.rle` | read + write | missing | low | [#915](https://github.com/Hawkynt/PNGCrushCS/issues/915) |
| DRAWX Vector Graphics (.vec) | Image | `.vec` | read + write | missing | low | [#916](https://github.com/Hawkynt/PNGCrushCS/issues/916) |
| Embedded Vector Editor (.eve) | Image | `.eve` | read + write | missing | low | [#917](https://github.com/Hawkynt/PNGCrushCS/issues/917) |
| Epson ESC/P and ESC/P2 raster printer streams | Image | `.prn`, `.esc` | read + write | missing | low | [#918](https://github.com/Hawkynt/PNGCrushCS/issues/918) |
| EVA (Extended Vector Animation, Sharp) | Image | `.eva` | read + write | missing | low | [#919](https://github.com/Hawkynt/PNGCrushCS/issues/919) |
| Flash XML Graphics (FXG) | Image | `.fxg` | read + write | missing | low | [#920](https://github.com/Hawkynt/PNGCrushCS/issues/920) |
| GLE (Graphics Layout Engine) script | Image | `.gle` | read + write | missing | low | [#921](https://github.com/Hawkynt/PNGCrushCS/issues/921) |
| GPFax FAX format | Image |  | read + write | missing | low | [#922](https://github.com/Hawkynt/PNGCrushCS/issues/922) |
| GST ART | Image | `.art` | read + write | missing | low | [#923](https://github.com/Hawkynt/PNGCrushCS/issues/923) |
| Haiku Vector Icon Format (HVIF) | Image | `.hvif`, `.iom` | read + write | missing | low | [#924](https://github.com/Hawkynt/PNGCrushCS/issues/924) |
| Harvard Graphics chart/show (CH3, SY3, CHT, TP3) | Image | `.ch3`, `.sy3`, `.cht`, `.tp3` | read + write | missing | low | [#925](https://github.com/Hawkynt/PNGCrushCS/issues/925) |
| HP PaintJet / PaintJet XL / ThinkJet printer streams | Image | `.pj`, `.prn` | read + write | missing | low | [#926](https://github.com/Hawkynt/PNGCrushCS/issues/926) |
| IconVG | Image | `.ivg` | read + write | missing | low | [#927](https://github.com/Hawkynt/PNGCrushCS/issues/927) |
| IFF DR2D (2D drawing) | Image | `.dr2d` | read + write | missing | low | [#928](https://github.com/Hawkynt/PNGCrushCS/issues/928) |
| IFF FAXX (Amiga facsimile) | Image | `.faxx`, `.fax` | read + write | missing | low | [#929](https://github.com/Hawkynt/PNGCrushCS/issues/929) |
| ImageMagick Vector Graphics (MVG) | Image | `.mvg` | read + write | missing | low | [#930](https://github.com/Hawkynt/PNGCrushCS/issues/930) |
| In-A-Vision PIC | Image | `.pic` | read + write | missing | low | [#931](https://github.com/Hawkynt/PNGCrushCS/issues/931) |
| IRIS CMYK Front End Processor CT | Image | `.ct` | read + write | missing | low | [#932](https://github.com/Hawkynt/PNGCrushCS/issues/932) |
| Legacy DOS/Windows/Amiga vector editors (KwikDraw, WinDraw, PWDraw, Professional Draw, ProShape, DrawStudio, Top Draw, Little Draw, LEONARD's Sketch, DeskMate Draw, ObjectGraphics, XVT-Draw, IntelliDraw, Mayura Draw, HiJaak PDW, DraftChoice-free formats) | Image | `.kwk`, `.pdw`, `.md`, `.idw`, `.fig`, `.tdr` | read + write | missing | low | [#933](https://github.com/Hawkynt/PNGCrushCS/issues/933) |
| Lotus 1-2-3 graph (PIC) | Image | `.pic` | read + write | missing | low | [#934](https://github.com/Hawkynt/PNGCrushCS/issues/934) |
| LuraDocument (LDF) | Image | `.ldf` | read + write | blocked | low | [#935](https://github.com/Hawkynt/PNGCrushCS/issues/935) |
| MacDraw / MacDraw II / ClarisDraw | Image | `.drw`, `.pict` | read + write | missing | low | [#936](https://github.com/Hawkynt/PNGCrushCS/issues/936) |
| Micrografx Draw / Designer (DRW, DSF, DS4) | Image | `.drw`, `.drt`, `.dsf`, `.ds4` | read + write | missing | low | [#937](https://github.com/Hawkynt/PNGCrushCS/issues/937) |
| Microsoft Office Document Imaging (MDI) | Image | `.mdi` | read + write | missing | low | [#938](https://github.com/Hawkynt/PNGCrushCS/issues/938) |
| Mixed Raster Content (ITU-T T.44) stand-alone | Image |  | read + write | missing | low | [#939](https://github.com/Hawkynt/PNGCrushCS/issues/939) |
| NAPLPS (North American Presentation Level Protocol Syntax) | Image | `.nap`, `.npl` | read + write | missing | low | [#940](https://github.com/Hawkynt/PNGCrushCS/issues/940) |
| NIFF (Navy Image File Format, CALS Type 3) | Image | `.nif`, `.ct3` | read + write | missing | low | [#941](https://github.com/Hawkynt/PNGCrushCS/issues/941) |
| OS/2 Presentation Manager Metafile (MET) | Image | `.met` | read + write | missing | low | [#942](https://github.com/Hawkynt/PNGCrushCS/issues/942) |
| Other DOS/Windows fax file formats (EZ-Fax .ezf, dexNET .dxn, Digifax/Zetafax .g3f/.g3s, eFax .efx, FAXability .pb, FaxMaker .fmk, FaxMaster .inm, faxSTF RFax/SFAX, FaxWorks .vmf, HotFax .hfx, Image System .ig4, J2 .jfx, JetFax .jet, Kofax DS .ds, Prometheus MaxFax FTIF, Product R&D .f01/.prd, RightFax .ndr, RingCentral .r0f, TEFAX .tef, TurboFax .tbf, Xerox MicroFax .mif, ZSoft .zgf) | Image | `.ezf`, `.dxn`, `.g3f`, `.g3s`, `.pb`, `.fmk` | read + write | missing | low | [#943](https://github.com/Hawkynt/PNGCrushCS/issues/943) |
| PaperPort scanned document (MAX) | Image | `.max` | read + write | missing | low | [#944](https://github.com/Hawkynt/PNGCrushCS/issues/944) |
| PCL XL (PCL 6 Enhanced) raster pages | Image | `.pxl`, `.pcl` | read + write | missing | low | [#945](https://github.com/Hawkynt/PNGCrushCS/issues/945) |
| Picroma Plasma | Image |  | read + write | missing | low | [#946](https://github.com/Hawkynt/PNGCrushCS/issues/946) |
| Pixie (vector graphics) | Image | `.pxi` | read + write | missing | low | [#947](https://github.com/Hawkynt/PNGCrushCS/issues/947) |
| PMDraw drawing (PMD) | Image | `.pmd` | read + write | missing | low | [#948](https://github.com/Hawkynt/PNGCrushCS/issues/948) |
| Print Artist / Instant Artist graphic (GFX) | Image | `.gfx` | read + write | missing | low | [#949](https://github.com/Hawkynt/PNGCrushCS/issues/949) |
| PrintPartner GPH | Image | `.gph` | read + write | missing | low | [#950](https://github.com/Hawkynt/PNGCrushCS/issues/950) |
| QuickLink II Fax (QFX) | Image | `.qfx` | read + write | missing | low | [#951](https://github.com/Hawkynt/PNGCrushCS/issues/951) |
| ReGIS (DEC Remote Graphic Instruction Set) | Image | `.regis` | read + write | missing | low | [#952](https://github.com/Hawkynt/PNGCrushCS/issues/952) |
| RIPscrip (Remote Imaging Protocol script) | Image | `.rip` | read + write | missing | low | [#953](https://github.com/Hawkynt/PNGCrushCS/issues/953) |
| SCODL (Scan Conversion Object Description Language) | Image |  | read + write | missing | low | [#954](https://github.com/Hawkynt/PNGCrushCS/issues/954) |
| Segmented Hypergraphics (SHG) and Multiple Resolution Bitmap (MRB) | Image | `.shg`, `.mrb` | read + write | missing | low | [#955](https://github.com/Hawkynt/PNGCrushCS/issues/955) |
| Separations output format (TeXnicard) and TeXnicard image mask | Image |  | read + write | missing | low | [#956](https://github.com/Hawkynt/PNGCrushCS/issues/956) |
| Serif DrawPlus (DPP) | Image | `.dpp` | read + write | missing | low | [#957](https://github.com/Hawkynt/PNGCrushCS/issues/957) |
| Simple Vector Format (SVF) | Image | `.svf` | read + write | missing | low | [#958](https://github.com/Hawkynt/PNGCrushCS/issues/958) |
| Skencil / sK1 drawing | Image | `.sk`, `.sk1` | read + write | missing | low | [#959](https://github.com/Hawkynt/PNGCrushCS/issues/959) |
| Sketch document (.sketch) | Image | `.sketch` | read + write | missing | low | [#960](https://github.com/Hawkynt/PNGCrushCS/issues/960) |
| SmartDraw document (SDR) | Image | `.sdr` | read + write | missing | low | [#961](https://github.com/Hawkynt/PNGCrushCS/issues/961) |
| Stanford Graphics | Image |  | read + write | missing | low | [#962](https://github.com/Hawkynt/PNGCrushCS/issues/962) |
| Synfig animation (SIF/SIFZ/SFG) | Image | `.sif`, `.sifz`, `.sfg` | read + write | missing | low | [#963](https://github.com/Hawkynt/PNGCrushCS/issues/963) |
| Tektronix 4010/4014 PLOT-10 graphics stream | Image | `.tek`, `.plt` | read + write | missing | low | [#964](https://github.com/Hawkynt/PNGCrushCS/issues/964) |
| Tgif drawing (.obj / .tgif) | Image | `.obj`, `.tgif` | read + write | missing | low | [#965](https://github.com/Hawkynt/PNGCrushCS/issues/965) |
| TIFF-FX (RFC 3949) fax profiles | Image | `.tif`, `.tfx` | read + write | missing | low | [#966](https://github.com/Hawkynt/PNGCrushCS/issues/966) |
| TIFF/IT (ISO 12639) prepress files | Image | `.fp`, `.ct`, `.lw`, `.hc`, `.bp`, `.bl` | read + write | missing | low | [#967](https://github.com/Hawkynt/PNGCrushCS/issues/967) |
| TinyVG | Image | `.tvg`, `.tvgt` | read + write | missing | low | [#968](https://github.com/Hawkynt/PNGCrushCS/issues/968) |
| Toon Boom Studio drawing/project (TBD, TBP) | Image | `.tbd`, `.tbp` | read + write | missing | low | [#969](https://github.com/Hawkynt/PNGCrushCS/issues/969) |
| Unix plot(5) / GNU plotutils metafile | Image | `.plot`, `.pl`, `.meta` | read + write | missing | low | [#970](https://github.com/Hawkynt/PNGCrushCS/issues/970) |
| Verity Image Format (VIF) | Image | `.vif` | read + write | missing | low | [#971](https://github.com/Hawkynt/PNGCrushCS/issues/971) |
| VML (Vector Markup Language) and PGML | Image | `.vml` | read + write | missing | low | [#972](https://github.com/Hawkynt/PNGCrushCS/issues/972) |
| Windows fax cover page (CPE) and WinFax cover page (CVP) | Image | `.cpe`, `.cvp` | read + write | missing | low | [#973](https://github.com/Hawkynt/PNGCrushCS/issues/973) |
| Xerox Paris LPG | Image | `.lpg` | read + write | blocked | low | [#974](https://github.com/Hawkynt/PNGCrushCS/issues/974) |
| Xfig FIG | Image | `.fig` | read + write | missing | low | [#975](https://github.com/Hawkynt/PNGCrushCS/issues/975) |
| XIFF / Xerox DigiPaper XIF | Image | `.xif` | read + write | missing | low | [#976](https://github.com/Hawkynt/PNGCrushCS/issues/976) |
| XPS / OpenXPS document page | Image | `.xps`, `.oxps` | read + write | missing | low | [#977](https://github.com/Hawkynt/PNGCrushCS/issues/977) |

## Fonts, text-mode & teletext

Bitmap/outline fonts, ANSI/text-mode art, teletext. Tracked in [#520](https://github.com/Hawkynt/PNGCrushCS/issues/520).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| OpenType font with CFF/CFF2 outlines | Image | `.otf` | read + write | missing | high | [#978](https://github.com/Hawkynt/PNGCrushCS/issues/978) |
| Adobe Type 1 font (PFA / PFB) | Image | `.pfa`, `.pfb`, `.pfm`, `.afm` | read + write | missing | medium | [#979](https://github.com/Hawkynt/PNGCrushCS/issues/979) |
| Glyph Bitmap Distribution Format (BDF) | Image | `.bdf` | read + write | missing | medium | [#980](https://github.com/Hawkynt/PNGCrushCS/issues/980) |
| PC Screen Font (PSF1 / PSF2) | Image | `.psf`, `.psfu`, `.psf.gz` | read + write | missing | medium | [#981](https://github.com/Hawkynt/PNGCrushCS/issues/981) |
| TrueType / OpenType Collection | Image | `.ttc`, `.otc` | read + write | missing | medium | [#982](https://github.com/Hawkynt/PNGCrushCS/issues/982) |
| Web Open Font Format 1.0 | Image | `.woff` | read + write | missing | medium | [#983](https://github.com/Hawkynt/PNGCrushCS/issues/983) |
| Web Open Font Format 2.0 | Image | `.woff2` | read + write | missing | medium | [#984](https://github.com/Hawkynt/PNGCrushCS/issues/984) |
| Windows bitmap font (FNT / FON) | Image | `.fnt`, `.fon` | read + write | missing | medium | [#985](https://github.com/Hawkynt/PNGCrushCS/issues/985) |
| X11 Portable Compiled Format (PCF) | Image | `.pcf`, `.pcf.gz` | read + write | missing | medium | [#986](https://github.com/Hawkynt/PNGCrushCS/issues/986) |
| Acorn RISC OS font (outline/bitmap) | Image |  | read + write | missing | low | [#987](https://github.com/Hawkynt/PNGCrushCS/issues/987) |
| Amiga bitmap font (.font + size files) | Image | `.font` | read + write | missing | low | [#988](https://github.com/Hawkynt/PNGCrushCS/issues/988) |
| ANSImation (animated ANSI) | Image | `.ans` | read + write | missing | low | [#989](https://github.com/Hawkynt/PNGCrushCS/issues/989) |
| Artworx Data Format (ADF) | Image | `.adf` | read + write | missing | low | [#990](https://github.com/Hawkynt/PNGCrushCS/issues/990) |
| Atari ST 8x16 system font | Image | `.fnt` | read + write | missing | low | [#991](https://github.com/Hawkynt/PNGCrushCS/issues/991) |
| AutoCAD shape/font (SHX / SHP) | Image | `.shx`, `.shp` | read + write | missing | low | [#992](https://github.com/Hawkynt/PNGCrushCS/issues/992) |
| AVATAR/0 (Advanced Video Attribute Terminal Assembler and Recreator) | Image | `.avt` | read + write | missing | low | [#993](https://github.com/Hawkynt/PNGCrushCS/issues/993) |
| Binary Text (BIN) ANSI-art screen dump | Image | `.bin` | read + write | missing | low | [#994](https://github.com/Hawkynt/PNGCrushCS/issues/994) |
| Bitstream Speedo font | Image | `.spd` | read + write | missing | low | [#995](https://github.com/Hawkynt/PNGCrushCS/issues/995) |
| Blazing Paddles font (Atari 8-bit) | Image | `.chr` | read + write | missing | low | [#996](https://github.com/Hawkynt/PNGCrushCS/issues/996) |
| Borland BGI stroked font (CHR) | Image | `.chr` | read + write | missing | low | [#997](https://github.com/Hawkynt/PNGCrushCS/issues/997) |
| Braille image output (BRF, ISO/TR 11548-1, Unicode Braille) | Image | `.brf`, `.ubrl`, `.isobrl` | read + write | missing | low | [#998](https://github.com/Hawkynt/PNGCrushCS/issues/998) |
| BSD/Sun vfont | Image |  | read + write | missing | low | [#999](https://github.com/Hawkynt/PNGCrushCS/issues/999) |
| Calamus font (CFN) | Image | `.cfn` | read + write | missing | low | [#1000](https://github.com/Hawkynt/PNGCrushCS/issues/1000) |
| Cebra Text teletext (TTX) | Image | `.ttx` | read + write | missing | low | [#1001](https://github.com/Hawkynt/PNGCrushCS/issues/1001) |
| ComAnsi (.anc) | Image | `.anc` | read + write | missing | low | [#1002](https://github.com/Hawkynt/PNGCrushCS/issues/1002) |
| DOS Code Page Information font (CPI) | Image | `.cpi`, `.cp`, `.cpx` | read + write | missing | low | [#1003](https://github.com/Hawkynt/PNGCrushCS/issues/1003) |
| DOS self-installing font executables (EVAfont, Font Mania, FONTEDIT) | Image | `.com` | read + write | missing | low | [#1004](https://github.com/Hawkynt/PNGCrushCS/issues/1004) |
| Embedded OpenType | Image | `.eot`, `.fntdata` | read + write | missing | low | [#1005](https://github.com/Hawkynt/PNGCrushCS/issues/1005) |
| FIGlet font | Image | `.flf`, `.tlf` | read + write | missing | low | [#1006](https://github.com/Hawkynt/PNGCrushCS/issues/1006) |
| FontForge Spline Font Database (SFD) | Image | `.sfd` | read + write | missing | low | [#1007](https://github.com/Hawkynt/PNGCrushCS/issues/1007) |
| GEM bitmap font / Atari GDOS font | Image | `.gft`, `.fnt` | read + write | missing | low | [#1008](https://github.com/Hawkynt/PNGCrushCS/issues/1008) |
| GEOS font (Commodore 64/128 CVT) | Image | `.cvt` | read + write | missing | low | [#1009](https://github.com/Hawkynt/PNGCrushCS/issues/1009) |
| GNU Unifont HEX | Image | `.hex` | read + write | missing | low | [#1010](https://github.com/Hawkynt/PNGCrushCS/issues/1010) |
| GRASP font | Image | `.set`, `.fnt` | read + write | missing | low | [#1011](https://github.com/Hawkynt/PNGCrushCS/issues/1011) |
| GRUB 2 PFF2 font | Image | `.pf2` | read + write | missing | low | [#1012](https://github.com/Hawkynt/PNGCrushCS/issues/1012) |
| iCEDraw (IDF) | Image | `.idf` | read + write | missing | low | [#1013](https://github.com/Hawkynt/PNGCrushCS/issues/1013) |
| Image 72 font (PC) | Image | `.fnt` | read + write | missing | low | [#1014](https://github.com/Hawkynt/PNGCrushCS/issues/1014) |
| ImageMagick TXT pixel enumeration | Image | `.txt` | read + write | missing | low | [#1015](https://github.com/Hawkynt/PNGCrushCS/issues/1015) |
| Instant Graphics and Sound (IG/IGS) | Image | `.ig`, `.igs` | read + write | missing | low | [#1016](https://github.com/Hawkynt/PNGCrushCS/issues/1016) |
| libvips matrix file (.mat) | Image | `.mat` | read + write | missing | low | [#1017](https://github.com/Hawkynt/PNGCrushCS/issues/1017) |
| Macintosh bitmap/outline font (NFNT/FONT, FOND, dfont suitcase) | Image | `.dfont`, `.suit` | read + write | missing | low | [#1018](https://github.com/Hawkynt/PNGCrushCS/issues/1018) |
| MegaZeux MZM | Image | `.mzm` | read + write | missing | low | [#1019](https://github.com/Hawkynt/PNGCrushCS/issues/1019) |
| MRG Systems teletext page (TTI) | Image | `.tti` | read + write | missing | low | [#1020](https://github.com/Hawkynt/PNGCrushCS/issues/1020) |
| Nitrogen Fingers Paint / Text (NFP, NFT) | Image | `.nfp`, `.nft` | read + write | missing | low | [#1021](https://github.com/Hawkynt/PNGCrushCS/issues/1021) |
| PCBoard @-code text (.PCB) | Image | `.pcb` | read + write | missing | low | [#1022](https://github.com/Hawkynt/PNGCrushCS/issues/1022) |
| PCR font (DOS) | Image | `.pcr` | read + write | missing | low | [#1023](https://github.com/Hawkynt/PNGCrushCS/issues/1023) |
| PIX (PIcture in teXt) | Image | `.pix` | read + write | missing | low | [#1024](https://github.com/Hawkynt/PNGCrushCS/issues/1024) |
| Portable Font Resource (TrueDoc PFR) | Image | `.pfr` | read + write | missing | low | [#1025](https://github.com/Hawkynt/PNGCrushCS/issues/1025) |
| Self-displaying DOS text screens (ACiDDraw COM, Laughing Dog .DOG/COM, P-Screen, OPTIKS, PCX2COM, GRABBER) | Image | `.com`, `.dog`, `.exe` | read + write | missing | low | [#1026](https://github.com/Hawkynt/PNGCrushCS/issues/1026) |
| Signum! font (Atari ST) | Image | `.e24`, `.p24`, `.l30`, `.p9` | read + write | missing | low | [#1027](https://github.com/Hawkynt/PNGCrushCS/issues/1027) |
| SNG - Scriptable Network Graphics | Image | `.sng` | read + write | missing | low | [#1028](https://github.com/Hawkynt/PNGCrushCS/issues/1028) |
| Softel teletext page (EP1) | Image | `.ep1` | read + write | missing | low | [#1029](https://github.com/Hawkynt/PNGCrushCS/issues/1029) |
| Teletext packets (T42) and raw teletext dumps | Image | `.t42`, `.bin` | read + write | missing | low | [#1030](https://github.com/Hawkynt/PNGCrushCS/issues/1030) |
| TeX Packed Font (PK) and Generic Font (GF) | Image | `.pk`, `.gf` | read + write | missing | low | [#1031](https://github.com/Hawkynt/PNGCrushCS/issues/1031) |
| TexFont texture-mapped font (TXF) | Image | `.txf` | read + write | missing | low | [#1032](https://github.com/Hawkynt/PNGCrushCS/issues/1032) |
| TheDraw font (TDF) | Image | `.tdf` | read + write | missing | low | [#1033](https://github.com/Hawkynt/PNGCrushCS/issues/1033) |
| TheDraw save file (.TD) and TheDraw COM | Image | `.td`, `.com` | read + write | missing | low | [#1034](https://github.com/Hawkynt/PNGCrushCS/issues/1034) |
| TUNDRA Draw (TND) | Image | `.tnd` | read + write | missing | low | [#1035](https://github.com/Hawkynt/PNGCrushCS/issues/1035) |
| Universal BitMap Format (UBF) font | Image | `.ubf` | read + write | missing | low | [#1036](https://github.com/Hawkynt/PNGCrushCS/issues/1036) |
| X-Motif UIL pixmap table | Image | `.uil` | read + write | missing | low | [#1037](https://github.com/Hawkynt/PNGCrushCS/issues/1037) |
| X11 Server Natural Format (SNF) | Image | `.snf` | read + write | missing | low | [#1038](https://github.com/Hawkynt/PNGCrushCS/issues/1038) |

## Editor & application native

Paint/photo editor project files, brushes, albums, thumbnails, OS/UI resources. Tracked in [#521](https://github.com/Hawkynt/PNGCrushCS/issues/521).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Adobe Photoshop brush (ABR) | Image | `.abr` | read + write | missing | medium | [#1039](https://github.com/Hawkynt/PNGCrushCS/issues/1039) |
| Classic Mac OS icon/cursor/pattern resources (ICN#, icl4/icl8, ics#, cicn, crsr, CURS, ppat, PICT in .rsrc) | Image | `.rsrc`, `.r` | read + write | missing | medium | [#1040](https://github.com/Hawkynt/PNGCrushCS/issues/1040) |
| CLIP STUDIO PAINT document (CLIP) / Manga Studio | Image | `.clip`, `.lip`, `.cmc` | read + write | missing | medium | [#1041](https://github.com/Hawkynt/PNGCrushCS/issues/1041) |
| GIMP image hose / animated brush (GIH) | Image | `.gih` | read + write | missing | medium | [#1042](https://github.com/Hawkynt/PNGCrushCS/issues/1042) |
| PaintTool SAI document (SAI, SAI2) | Image | `.sai`, `.sai2` | read + write | missing | medium | [#1043](https://github.com/Hawkynt/PNGCrushCS/issues/1043) |
| Procreate document | Image | `.procreate` | read + write | missing | medium | [#1044](https://github.com/Hawkynt/PNGCrushCS/issues/1044) |
| Adobe Fireworks PNG (editable layers) | Image | `.png`, `.fw.png` | read + write | missing | low | [#1045](https://github.com/Hawkynt/PNGCrushCS/issues/1045) |
| Adobe Photoshop pattern (PAT, 8BPT) | Image | `.pat` | read + write | missing | low | [#1046](https://github.com/Hawkynt/PNGCrushCS/issues/1046) |
| Amiga GlowIcons / OS3.5 ColorIcons and OS4 PNG icons | Image | `.info` | read + write | missing | low | [#1047](https://github.com/Hawkynt/PNGCrushCS/issues/1047) |
| Amiga NewIcons (ToolType-encoded) | Image | `.info` | read + write | missing | low | [#1048](https://github.com/Hawkynt/PNGCrushCS/issues/1048) |
| ArtRage painting (PTG) | Image | `.ptg` | read + write | missing | low | [#1049](https://github.com/Hawkynt/PNGCrushCS/issues/1049) |
| Be/Haiku TranslatorBitmap (bits) | Image | `.bits` | read + write | missing | low | [#1050](https://github.com/Hawkynt/PNGCrushCS/issues/1050) |
| BIF (Image Alchemy binary information file) | Image | `.bif` | read + write | missing | low | [#1051](https://github.com/Hawkynt/PNGCrushCS/issues/1051) |
| Chasys Draw IES image (CD5) | Image | `.cd5` | read + write | missing | low | [#1052](https://github.com/Hawkynt/PNGCrushCS/issues/1052) |
| Comic book archives (CBZ, CBR, CB7, CBT, CBA) | Image | `.cbz`, `.cbr`, `.cb7`, `.cbt`, `.cba` | read + write | missing | low | [#1053](https://github.com/Hawkynt/PNGCrushCS/issues/1053) |
| Corel Photo House (CPS) | Image | `.cps` | read + write | missing | low | [#1054](https://github.com/Hawkynt/PNGCrushCS/issues/1054) |
| Corel/Fractal Design Painter RIFF (.rif) | Image | `.rif`, `.riff` | read + write | missing | low | [#1055](https://github.com/Hawkynt/PNGCrushCS/issues/1055) |
| Dorling Kindersley SBMP | Image | `.sbm` | read + write | missing | low | [#1056](https://github.com/Hawkynt/PNGCrushCS/issues/1056) |
| Doré Raster | Image | `.dore`, `.img` | read + write | missing | low | [#1057](https://github.com/Hawkynt/PNGCrushCS/issues/1057) |
| Esri Compact Cache (bundle) | Image | `.bundle`, `.bundlx` | read + write | missing | low | [#1058](https://github.com/Hawkynt/PNGCrushCS/issues/1058) |
| GEM resource file (RSC) icons and images | Image | `.rsc` | read + write | missing | low | [#1059](https://github.com/Hawkynt/PNGCrushCS/issues/1059) |
| GeoPackage raster tiles | Image | `.gpkg` | read + write | missing | low | [#1060](https://github.com/Hawkynt/PNGCrushCS/issues/1060) |
| Graphic Workshop thumbnail (.thn) | Image | `.thn` | read + write | missing | low | [#1061](https://github.com/Hawkynt/PNGCrushCS/issues/1061) |
| HP Photo Album (ALBM) | Image | `.albm` | read + write | missing | low | [#1062](https://github.com/Hawkynt/PNGCrushCS/issues/1062) |
| LazPaint image (LZP) | Image | `.lzp` | read + write | missing | low | [#1063](https://github.com/Hawkynt/PNGCrushCS/issues/1063) |
| MBTiles (raster tiles in SQLite) | Image | `.mbtiles` | read + write | missing | low | [#1064](https://github.com/Hawkynt/PNGCrushCS/issues/1064) |
| MediBang Paint / FireAlpaca document (MDP) | Image | `.mdp` | read + write | missing | low | [#1065](https://github.com/Hawkynt/PNGCrushCS/issues/1065) |
| Microsoft Image Composer (MIC) | Image | `.mic` | read + write | missing | low | [#1066](https://github.com/Hawkynt/PNGCrushCS/issues/1066) |
| openCanvas document (WPB, OCI) | Image | `.wpb`, `.oci` | read + write | missing | low | [#1067](https://github.com/Hawkynt/PNGCrushCS/issues/1067) |
| OS/2 bitmap array, icon and pointer (BA / IC / CI / PT / CP) | Image | `.ico`, `.ptr`, `.bmp` | read + write | missing | low | [#1068](https://github.com/Hawkynt/PNGCrushCS/issues/1068) |
| Piskel sprite (.piskel) | Image | `.piskel` | read + write | missing | low | [#1069](https://github.com/Hawkynt/PNGCrushCS/issues/1069) |
| Pixelmator (PXM) and Pixelmator Pro (PXD) | Image | `.pxm`, `.pxd` | read + write | missing | low | [#1070](https://github.com/Hawkynt/PNGCrushCS/issues/1070) |
| Pixiv Ugoira | Image | `.zip`, `.ugoira` | read + write | missing | low | [#1071](https://github.com/Hawkynt/PNGCrushCS/issues/1071) |
| Pixlr document (PXD) | Image | `.pxd` | read + write | missing | low | [#1072](https://github.com/Hawkynt/PNGCrushCS/issues/1072) |
| Pyxel Edit document (.pyxel) | Image | `.pyxel` | read + write | missing | low | [#1073](https://github.com/Hawkynt/PNGCrushCS/issues/1073) |
| RLI (RealWorld Layered Image) | Image | `.rli` | read + write | missing | low | [#1074](https://github.com/Hawkynt/PNGCrushCS/issues/1074) |
| Second Nature screensaver graphic (SNX) | Image | `.snx` | read + write | missing | low | [#1075](https://github.com/Hawkynt/PNGCrushCS/issues/1075) |
| Sony PictureGear Studio projects (AMU, LMU, BXU) | Image | `.amu`, `.lmu`, `.bxu` | read + write | missing | low | [#1076](https://github.com/Hawkynt/PNGCrushCS/issues/1076) |
| Spaceward Graphics / Satori (.r, .rir) | Image | `.r`, `.rir` | read + write | missing | low | [#1077](https://github.com/Hawkynt/PNGCrushCS/issues/1077) |
| Thumbnail cache databases (Thumbs.db, thumbcache_*.db, iThmb, ThumbsPlus, ZoomBrowser, Photoshop cache, SuperJPG) | Image | `.db`, `.ithmb`, `.tud`, `.info`, `.tnc` | read + write | missing | low | [#1078](https://github.com/Hawkynt/PNGCrushCS/issues/1078) |
| TVPaint project (TVPP) | Image | `.tvpp` | read + write | missing | low | [#1079](https://github.com/Hawkynt/PNGCrushCS/issues/1079) |
| Ulead Photo Express UPX/TPX | Image | `.upx`, `.tpx` | read + write | missing | low | [#1080](https://github.com/Hawkynt/PNGCrushCS/issues/1080) |
| Ulead PhotoImpact object file (UFO) | Image | `.ufo` | read + write | missing | low | [#1081](https://github.com/Hawkynt/PNGCrushCS/issues/1081) |
| Vizrt Binary Image (VBN) | Image | `.vbn` | read + write | missing | low | [#1082](https://github.com/Hawkynt/PNGCrushCS/issues/1082) |
| Windows compiled resource (.res) bitmaps/icons/cursors | Image | `.res` | read + write | missing | low | [#1083](https://github.com/Hawkynt/PNGCrushCS/issues/1083) |
| X-Face | Image | `xface`, `X-Face:` | read + write | missing | low | [#1084](https://github.com/Hawkynt/PNGCrushCS/issues/1084) |
| XNG (animated) | Image | `.xng` | read + write | missing | low | [#1085](https://github.com/Hawkynt/PNGCrushCS/issues/1085) |
| xRes LRG | Image | `.lrg` | read + write | missing | low | [#1086](https://github.com/Hawkynt/PNGCrushCS/issues/1086) |
| Zoner bitmap (BMI) | Image | `.bmi` | read + write | missing | low | [#1087](https://github.com/Hawkynt/PNGCrushCS/issues/1087) |

## Embroidery & industrial

Embroidery machine stitch files. Tracked in [#522](https://github.com/Hawkynt/PNGCrushCS/issues/522).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Brother PEC (stand-alone) | Image | `.pec` | read + write | missing | medium | [#1088](https://github.com/Hawkynt/PNGCrushCS/issues/1088) |
| Husqvarna Viking / Pfaff VP3 | Image | `.vp3` | read + write | missing | medium | [#1089](https://github.com/Hawkynt/PNGCrushCS/issues/1089) |
| Janome JEF / JEF+ | Image | `.jef` | read + write | missing | medium | [#1090](https://github.com/Hawkynt/PNGCrushCS/issues/1090) |
| Melco Expanded (EXP) | Image | `.exp` | read + write | missing | medium | [#1091](https://github.com/Hawkynt/PNGCrushCS/issues/1091) |
| Tajima DST embroidery | Image | `.dst` | read + write | missing | medium | [#1092](https://github.com/Hawkynt/PNGCrushCS/issues/1092) |
| Barudan U01 / FDR and Barudan/Sunstar DAT | Image | `.u01`, `.u00`, `.dat` | read + write | missing | low | [#1093](https://github.com/Hawkynt/PNGCrushCS/issues/1093) |
| Bernina ART | Image | `.art` | read + write | missing | low | [#1094](https://github.com/Hawkynt/PNGCrushCS/issues/1094) |
| Brother PHB, PHC and PMV | Image | `.phb`, `.phc`, `.pmv` | read + write | missing | low | [#1095](https://github.com/Hawkynt/PNGCrushCS/issues/1095) |
| Husqvarna Viking / Pfaff VP4 | Image | `.vp4` | read + write | missing | low | [#1096](https://github.com/Hawkynt/PNGCrushCS/issues/1096) |
| Husqvarna Viking HUS and VIP | Image | `.hus`, `.vip` | read + write | missing | low | [#1097](https://github.com/Hawkynt/PNGCrushCS/issues/1097) |
| Husqvarna Viking SHV | Image | `.shv` | read + write | missing | low | [#1098](https://github.com/Hawkynt/PNGCrushCS/issues/1098) |
| Janome SEW | Image | `.sew` | read + write | missing | low | [#1099](https://github.com/Hawkynt/PNGCrushCS/issues/1099) |
| Melco DesignShop OFM | Image | `.ofm` | read + write | missing | low | [#1100](https://github.com/Hawkynt/PNGCrushCS/issues/1100) |
| Minor embroidery machine formats (Toyota 10o/100, Bits&Volts BRO, Elna EMD, Eltac EXY, Fortron FXY, Gold Thread GT, Inbro INB, Mitsubishi MIT, Ameco NEW, Gunold STC, Data Stitch STX, Happy TAP, Zeng Hsing ZHS, Janome JPX, SPX) | Image | `.10o`, `.100`, `.bro`, `.emd`, `.exy`, `.fxy` | read + write | missing | low | [#1101](https://github.com/Hawkynt/PNGCrushCS/issues/1101) |
| Pfaff PCS, PCD, PCM, PCQ, KSM, MAX | Image | `.pcs`, `.pcd`, `.pcm`, `.pcq`, `.ksm`, `.max` | read + write | missing | low | [#1102](https://github.com/Hawkynt/PNGCrushCS/issues/1102) |
| Singer / Compucon XXX | Image | `.xxx` | read + write | missing | low | [#1103](https://github.com/Hawkynt/PNGCrushCS/issues/1103) |
| Singer / Husqvarna CSD (POEM, Huskygram) | Image | `.csd` | read + write | missing | low | [#1104](https://github.com/Hawkynt/PNGCrushCS/issues/1104) |
| Tajima TBF | Image | `.tbf` | read + write | missing | low | [#1105](https://github.com/Hawkynt/PNGCrushCS/issues/1105) |
| Wilcom ES / EmbroideryStudio EMB | Image | `.emb` | read + write | missing | low | [#1106](https://github.com/Hawkynt/PNGCrushCS/issues/1106) |
| ZSK DSZ / DSB / ZXY | Image | `.dsz`, `.dsb`, `.zxy` | read + write | missing | low | [#1107](https://github.com/Hawkynt/PNGCrushCS/issues/1107) |

## Standard & broadcast video

ITU/ISO/SMPTE/AOM codecs, broadcast and professional intermediates, streaming. Tracked in [#523](https://github.com/Hawkynt/PNGCrushCS/issues/523).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Advanced Professional Video (APV) | Codec | `.apv`, `.mp4` | decode + encode | missing | high | [#1108](https://github.com/Hawkynt/PNGCrushCS/issues/1108) |
| AOMedia Video 2 (AV2) | Codec | `av02`, `AV2` | decode + encode | missing | high | [#1109](https://github.com/Hawkynt/PNGCrushCS/issues/1109) |
| AV1 (full video decoder) | Codec | `.obu`, `.ivf`, `.mp4`, `.mkv`, `.webm` | decode + encode | missing | high | [#1110](https://github.com/Hawkynt/PNGCrushCS/issues/1110) |
| H.266 / VVC (Versatile Video Coding) | Codec | `.266`, `.vvc`, `.h266`, `.mp4`, `.mkv`, `.ts` | decode + encode | missing | high | [#1111](https://github.com/Hawkynt/PNGCrushCS/issues/1111) |
| RealVideo 4.0 (RV40) | Codec | `.rm`, `.rmvb` | decode + encode | missing | high | [#1113](https://github.com/Hawkynt/PNGCrushCS/issues/1113) |
| VC-1 Advanced Profile, interlace, and WMV3 Complex Profile | Codec | `.wmv`, `.vc1`, `.rcv`, `.mkv`, `.m2ts`, `.evo` | decode + encode | missing | high | [#1114](https://github.com/Hawkynt/PNGCrushCS/issues/1114) |
| Material eXchange Format (MXF) | Container | `.mxf` | demux + mux | missing | high | [#1112](https://github.com/Hawkynt/PNGCrushCS/issues/1112) |
| Apple Intermediate Codec | Codec | `.mov` | decode + encode | missing | medium | [#1115](https://github.com/Hawkynt/PNGCrushCS/issues/1115) |
| Dirac / SMPTE VC-2 | Codec | `.drc` | decode + encode | missing | medium | [#1116](https://github.com/Hawkynt/PNGCrushCS/issues/1116) |
| Dolby Vision (RPU metadata + enhancement layer) | Codec | `dvhe`, `dvh1`, `dvav` | decode + encode | missing | medium | [#1117](https://github.com/Hawkynt/PNGCrushCS/issues/1117) |
| JPEG XS (ISO/IEC 21122) | Codec | `.jxs`, `.mxf`, `.mp4` | decode + encode | missing | medium | [#1120](https://github.com/Hawkynt/PNGCrushCS/issues/1120) |
| NewTek SpeedHQ | Codec | `.avi`, `.shq2` | decode + encode | blocked | medium | [#1122](https://github.com/Hawkynt/PNGCrushCS/issues/1122) |
| NotchLC | Codec | `.mov` | decode + encode | missing | medium | [#1123](https://github.com/Hawkynt/PNGCrushCS/issues/1123) |
| RealVideo 2.0 (RV20) | Codec | `.rm`, `.rmvb` | decode + encode | missing | medium | [#1125](https://github.com/Hawkynt/PNGCrushCS/issues/1125) |
| RealVideo 3.0 (RV30) | Codec | `.rm`, `.rmvb` | decode + encode | missing | medium | [#1126](https://github.com/Hawkynt/PNGCrushCS/issues/1126) |
| Resolume DXV | Codec | `.mov` | decode + encode | missing | medium | [#1127](https://github.com/Hawkynt/PNGCrushCS/issues/1127) |
| Scalable HEVC (SHVC) and MV-HEVC multilayer decoding | Codec | `SHVC`, `MV-HEVC`, `nuh_layer_id>0` | decode + encode | missing | medium | [#1128](https://github.com/Hawkynt/PNGCrushCS/issues/1128) |
| Windows Media Video 7 (WMV1) | Codec | `.wmv`, `.asf` | decode + encode | blocked | medium | [#1129](https://github.com/Hawkynt/PNGCrushCS/issues/1129) |
| Windows Media Video 8 (WMV2) | Codec | `.wmv`, `.asf` | decode + encode | blocked | medium | [#1130](https://github.com/Hawkynt/PNGCrushCS/issues/1130) |
| General eXchange Format (GXF) | Container | `.gxf` | demux + mux | missing | medium | [#1118](https://github.com/Hawkynt/PNGCrushCS/issues/1118) |
| Interoperable Master Format (IMF) composition | Container | `.xml` | demux + mux | missing | medium | [#1119](https://github.com/Hawkynt/PNGCrushCS/issues/1119) |
| Motion JPEG 2000 (MJ2) | Container | `.mj2`, `.mjp2` | demux + mux | missing | medium | [#1121](https://github.com/Hawkynt/PNGCrushCS/issues/1121) |
| NUT | Container | `.nut` | demux + mux | missing | medium | [#1124](https://github.com/Hawkynt/PNGCrushCS/issues/1124) |
| Apple Pixlet | Codec | `.mov` | decode + encode | missing | low | [#1132](https://github.com/Hawkynt/PNGCrushCS/issues/1132) |
| AVS2 video (IEEE 1857.4 / GB/T 33475.2) | Codec | `.avs2` | decode + encode | missing | low | [#1133](https://github.com/Hawkynt/PNGCrushCS/issues/1133) |
| AVS3 video (IEEE 1857.10) | Codec | `.avs3` | decode + encode | missing | low | [#1134](https://github.com/Hawkynt/PNGCrushCS/issues/1134) |
| BitJazz SheerVideo | Codec | `.mov` | decode + encode | blocked | low | [#1135](https://github.com/Hawkynt/PNGCrushCS/issues/1135) |
| Canopus HQ / HQA | Codec | `.avi` | decode + encode | blocked | low | [#1137](https://github.com/Hawkynt/PNGCrushCS/issues/1137) |
| Canopus HQX | Codec | `.avi` | decode + encode | blocked | low | [#1138](https://github.com/Hawkynt/PNGCrushCS/issues/1138) |
| CD-i Delta YUV (DYUV) video | Codec | `DYUV` | decode + encode | missing | low | [#1139](https://github.com/Hawkynt/PNGCrushCS/issues/1139) |
| Chinese AVS1-P2 (JiZhun) / AVS+ | Codec | `.cavs`, `.avs` | decode + encode | missing | low | [#1140](https://github.com/Hawkynt/PNGCrushCS/issues/1140) |
| Cisco Thor | Codec | `thor`, `NETVC` | decode + encode | missing | low | [#1141](https://github.com/Hawkynt/PNGCrushCS/issues/1141) |
| Daala | Codec | `.ogg`, `.ogv` | decode + encode | missing | low | [#1142](https://github.com/Hawkynt/PNGCrushCS/issues/1142) |
| H.264 Multiview Video Coding (Annex H, Blu-ray 3D) | Codec | `MVC`, `NAL 20`, `mvc1` | decode + encode | missing | low | [#1145](https://github.com/Hawkynt/PNGCrushCS/issues/1145) |
| H.264 Scalable Video Coding (Annex G) | Codec | `SVC`, `NAL 14/20`, `avc1 + svc1` | decode + encode | missing | low | [#1146](https://github.com/Hawkynt/PNGCrushCS/issues/1146) |
| Media 100i | Codec | `.mov` | decode + encode | missing | low | [#1149](https://github.com/Hawkynt/PNGCrushCS/issues/1149) |
| MPEG-5 EVC (Essential Video Coding) | Codec | `.evc`, `.mp4` | decode + encode | missing | low | [#1150](https://github.com/Hawkynt/PNGCrushCS/issues/1150) |
| MPEG-5 LCEVC (ISO/IEC 23094-2) | Codec | `.lcevc`, `.ts`, `.mp4`, `.mkv` | decode + encode | missing | low | [#1151](https://github.com/Hawkynt/PNGCrushCS/issues/1151) |
| RealVideo 6.0 (RV60 / RealVideo HD) | Codec | `.rmhd`, `.rm` | decode + encode | missing | low | [#1154](https://github.com/Hawkynt/PNGCrushCS/issues/1154) |
| Snow | Codec | `.avi`, `.nut`, `.mkv` | decode + encode | missing | low | [#1156](https://github.com/Hawkynt/PNGCrushCS/issues/1156) |
| Streambox ACT-L2 / ACT-L3 | Codec | `ACTL`, `ACT-L3` | decode + encode | missing | low | [#1157](https://github.com/Hawkynt/PNGCrushCS/issues/1157) |
| vMix Video (VMX1) | Codec | `.avi`, `.mov` | decode + encode | missing | low | [#1160](https://github.com/Hawkynt/PNGCrushCS/issues/1160) |
| Adobe Filmstrip | Container | `.flm` | demux + mux | missing | low | [#1131](https://github.com/Hawkynt/PNGCrushCS/issues/1131) |
| Blu-ray movie playlist (MPLS) and clip info (CLPI) | Container | `.mpls`, `.clpi` | demux + mux | missing | low | [#1136](https://github.com/Hawkynt/PNGCrushCS/issues/1136) |
| DirectShow Media (DSM) | Container | `.dsm` | demux + mux | missing | low | [#1143](https://github.com/Hawkynt/PNGCrushCS/issues/1143) |
| FIASCO fractal video/image | Container | `.fco`, `.wfa` | demux + mux | missing | low | [#1144](https://github.com/Hawkynt/PNGCrushCS/issues/1144) |
| Leitch/Harris VR native stream (LXF) | Container | `.lxf` | demux + mux | missing | low | [#1147](https://github.com/Hawkynt/PNGCrushCS/issues/1147) |
| MAGIX Video (MXV) | Container | `.mxv` | demux + mux | missing | low | [#1148](https://github.com/Hawkynt/PNGCrushCS/issues/1148) |
| Nullsoft Streaming Video (NSV) | Container | `.nsv` | demux + mux | missing | low | [#1152](https://github.com/Hawkynt/PNGCrushCS/issues/1152) |
| ratDVD / XVO | Container | `.ratdvd`, `.xvo` | demux + mux | missing | low | [#1153](https://github.com/Hawkynt/PNGCrushCS/issues/1153) |
| SER astronomical capture | Container | `.ser` | demux + mux | missing | low | [#1155](https://github.com/Hawkynt/PNGCrushCS/issues/1155) |
| Vividas VIV | Container | `.viv` | demux + mux | missing | low | [#1158](https://github.com/Hawkynt/PNGCrushCS/issues/1158) |
| Vivo Active (VIV) | Container | `.viv` | demux + mux | missing | low | [#1159](https://github.com/Hawkynt/PNGCrushCS/issues/1159) |

## Game & multimedia-title video

Codecs/containers from games, consoles, CD-ROM titles and animation tools. Tracked in [#524](https://github.com/Hawkynt/PNGCrushCS/issues/524).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Bink Video (Bink 1) | Codec | `.bik` | decode + encode | missing | medium | [#1161](https://github.com/Hawkynt/PNGCrushCS/issues/1161) |
| Bink Video 2 | Codec | `KB2a`, `KB2b`, `KB2c` | decode + encode | missing | medium | [#1162](https://github.com/Hawkynt/PNGCrushCS/issues/1162) |
| CD+G karaoke graphics | Codec | `.cdg` | decode + encode | missing | medium | [#1163](https://github.com/Hawkynt/PNGCrushCS/issues/1163) |
| Sony PlayStation MDEC | Codec | `.str` | decode + encode | missing | medium | [#1166](https://github.com/Hawkynt/PNGCrushCS/issues/1166) |
| RAD Bink / Bink 2 container | Container | `.bik`, `.bk2` | demux + mux | missing | medium | [#1164](https://github.com/Hawkynt/PNGCrushCS/issues/1164) |
| Smacker Video encoder | Encoder | `SMK2`, `SMK4` | encode | decoder only | medium | [#1165](https://github.com/Hawkynt/PNGCrushCS/issues/1165) |
| 4X Movie video | Codec | `.4xm` | decode + encode | missing | low | [#1168](https://github.com/Hawkynt/PNGCrushCS/issues/1168) |
| 8088flex TMV | Codec | `.tmv` | decode + encode | blocked | low | [#1171](https://github.com/Hawkynt/PNGCrushCS/issues/1171) |
| Amazing Studio Packed Animation File Video | Codec | `.paf` | decode + encode | missing | low | [#1181](https://github.com/Hawkynt/PNGCrushCS/issues/1181) |
| American Laser Games MM Video | Codec | `.mm` | decode + encode | missing | low | [#1184](https://github.com/Hawkynt/PNGCrushCS/issues/1184) |
| Argonaut Games BRP video (BVID) | Codec | `.brp` | decode + encode | missing | low | [#1191](https://github.com/Hawkynt/PNGCrushCS/issues/1191) |
| Beam Software VB Video | Codec | `.vb`, `.son` | decode + encode | missing | low | [#1199](https://github.com/Hawkynt/PNGCrushCS/issues/1199) |
| Bethesda VID video | Codec | `.vid` | decode + encode | missing | low | [#1201](https://github.com/Hawkynt/PNGCrushCS/issues/1201) |
| Bitmap Brothers JV video | Codec | `.jv` | decode + encode | missing | low | [#1203](https://github.com/Hawkynt/PNGCrushCS/issues/1203) |
| CDToons (Broderbund Living Books) | Codec | `.mov` | decode + encode | missing | low | [#1210](https://github.com/Hawkynt/PNGCrushCS/issues/1210) |
| Chronomaster DFA | Codec | `.dfa` | decode + encode | blocked | low | [#1213](https://github.com/Hawkynt/PNGCrushCS/issues/1213) |
| Cinepak for Sega | Codec | `sega`, `SEGA` | decode + encode | missing | low | [#1215](https://github.com/Hawkynt/PNGCrushCS/issues/1215) |
| Creature Shock AVS | Codec | `.avs` | decode + encode | missing | low | [#1219](https://github.com/Hawkynt/PNGCrushCS/issues/1219) |
| CRI P256 | Codec | `P256`, `B256` | decode + encode | missing | low | [#1220](https://github.com/Hawkynt/PNGCrushCS/issues/1220) |
| Cryo HNM4 video | Codec | `.hnm` | decode + encode | missing | low | [#1225](https://github.com/Hawkynt/PNGCrushCS/issues/1225) |
| Delphine Software CIN video | Codec | `.cin` | decode + encode | missing | low | [#1235](https://github.com/Hawkynt/PNGCrushCS/issues/1235) |
| Deluxe Paint Animation (ANM) | Codec | `.anm` | decode + encode | blocked | low | [#1237](https://github.com/Hawkynt/PNGCrushCS/issues/1237) |
| Digital Pictures SGA Video | Codec | `.sga` | decode + encode | missing | low | [#1241](https://github.com/Hawkynt/PNGCrushCS/issues/1241) |
| Discworld II BMV video | Codec | `.bmv` | decode + encode | missing | low | [#1243](https://github.com/Hawkynt/PNGCrushCS/issues/1243) |
| Eidos Escape 122 | Codec | `ESCP 122` | decode + encode | missing | low | [#1251](https://github.com/Hawkynt/PNGCrushCS/issues/1251) |
| Electronic Arts Madcow Video (MAD) | Codec | `.mad` | decode + encode | blocked | low | [#1253](https://github.com/Hawkynt/PNGCrushCS/issues/1253) |
| Electronic Arts TGQ Video | Codec | `.tgq`, `.uv` | decode + encode | blocked | low | [#1254](https://github.com/Hawkynt/PNGCrushCS/issues/1254) |
| Electronic Arts TGV Video | Codec | `.tgv` | decode + encode | blocked | low | [#1255](https://github.com/Hawkynt/PNGCrushCS/issues/1255) |
| Electronic Arts TQI Video | Codec | `.tqi`, `.uv2`, `.wve` | decode + encode | blocked | low | [#1256](https://github.com/Hawkynt/PNGCrushCS/issues/1256) |
| Feeble Files / ScummVM DXA | Codec | `.dxa` | decode + encode | missing | low | [#1262](https://github.com/Hawkynt/PNGCrushCS/issues/1262) |
| Gremlin Digital Video | Codec | `.gdv` | decode + encode | missing | low | [#1272](https://github.com/Hawkynt/PNGCrushCS/issues/1272) |
| Gryphon Software ARBC | Codec | `arbc`, `ARBC` | decode + encode | missing | low | [#1274](https://github.com/Hawkynt/PNGCrushCS/issues/1274) |
| Imagination Pilots Matte Animation (Ipma, Ip20) | Codec | `Ipma`, `Ip20` | decode + encode | missing | low | [#1286](https://github.com/Hawkynt/PNGCrushCS/issues/1286) |
| Interplay C93 (Cyberia) | Codec | `.c93` | decode + encode | missing | low | [#1292](https://github.com/Hawkynt/PNGCrushCS/issues/1292) |
| Karl Morton's Video Codec | Codec | `.avi` | decode + encode | missing | low | [#1300](https://github.com/Hawkynt/PNGCrushCS/issues/1300) |
| Kega Game Video (KGV1) | Codec | `.avi` | decode + encode | missing | low | [#1301](https://github.com/Hawkynt/PNGCrushCS/issues/1301) |
| LucasArts SANM/SMUSH Video | Codec | `.san`, `.snm`, `.znm` | decode + encode | missing | low | [#1312](https://github.com/Hawkynt/PNGCrushCS/issues/1312) |
| MobiClip Video | Codec | `.moflex`, `.mods` | decode + encode | missing | low | [#1325](https://github.com/Hawkynt/PNGCrushCS/issues/1325) |
| Nintendo GameCube THP Video | Codec | `.thp` | decode + encode | missing | low | [#1335](https://github.com/Hawkynt/PNGCrushCS/issues/1335) |
| Origin Flic Codec (JYV1, JYA1, RRV1, RRV2) | Codec | `JYV1`, `JYA1`, `RRV1` | decode + encode | missing | low | [#1339](https://github.com/Hawkynt/PNGCrushCS/issues/1339) |
| PDQ2 | Codec | `PDQ2` | decode + encode | missing | low | [#1344](https://github.com/Hawkynt/PNGCrushCS/issues/1344) |
| Pixar QuickTime codecs (pix0-pix4) | Codec | `pix0`, `pix1`, `pix2` | decode + encode | missing | low | [#1345](https://github.com/Hawkynt/PNGCrushCS/issues/1345) |
| Playdate Video (PDV) | Codec | `.pdv` | decode + encode | missing | low | [#1346](https://github.com/Hawkynt/PNGCrushCS/issues/1346) |
| PlayStation 2 IPU video | Codec | `.ipu` | decode + encode | missing | low | [#1347](https://github.com/Hawkynt/PNGCrushCS/issues/1347) |
| Psygnosis YOP Video | Codec | `.yop` | decode + encode | missing | low | [#1358](https://github.com/Hawkynt/PNGCrushCS/issues/1358) |
| RL2 Video | Codec | `.rl2` | decode + encode | missing | low | [#1365](https://github.com/Hawkynt/PNGCrushCS/issues/1365) |
| Simbiosis Interactive IMX Video | Codec | `.imx` | decode + encode | missing | low | [#1375](https://github.com/Hawkynt/PNGCrushCS/issues/1375) |
| Tiertex Limited SEQ Video | Codec | `.seq` | decode + encode | missing | low | [#1395](https://github.com/Hawkynt/PNGCrushCS/issues/1395) |
| Wing Commander III Xan Video | Codec | `.mve` | decode + encode | missing | low | [#1407](https://github.com/Hawkynt/PNGCrushCS/issues/1407) |
| Wing Commander IV Xxan Video | Codec | `.avi` | decode + encode | missing | low | [#1408](https://github.com/Hawkynt/PNGCrushCS/issues/1408) |
| 3DO STR stream | Container | `.str` | demux + mux | missing | low | [#1167](https://github.com/Hawkynt/PNGCrushCS/issues/1167) |
| 4X Technologies 4XM movie | Container | `.4xm` | demux + mux | missing | low | [#1169](https://github.com/Hawkynt/PNGCrushCS/issues/1169) |
| 7th Level movie (Monty Python and the Holy Grail) | Container | `.mov_toc`, `.mov_data` | demux + mux | missing | low | [#1170](https://github.com/Hawkynt/PNGCrushCS/issues/1170) |
| 8088flex TMV | Container | `.tmv` | demux + mux | blocked | low | [#1172](https://github.com/Hawkynt/PNGCrushCS/issues/1172) |
| A/X-101 BIN (Sega CD) | Container | `.bin` | demux + mux | missing | low | [#1173](https://github.com/Hawkynt/PNGCrushCS/issues/1173) |
| Access Software VID | Container | `.vid` | demux + mux | missing | low | [#1174](https://github.com/Hawkynt/PNGCrushCS/issues/1174) |
| ACG Movie (overlay animation) | Container | `.wdf`, `.m24` | demux + mux | missing | low | [#1175](https://github.com/Hawkynt/PNGCrushCS/issues/1175) |
| Actimagine VX (Nintendo DS / GBA) | Container | `.vx` | demux + mux | missing | low | [#1176](https://github.com/Hawkynt/PNGCrushCS/issues/1176) |
| Adeline Time Commando ACF (XCF) | Container | `.acf` | demux + mux | missing | low | [#1177](https://github.com/Hawkynt/PNGCrushCS/issues/1177) |
| Adorage SSA / ClariSSA (Amiga) | Container | `.ssa` | demux + mux | missing | low | [#1178](https://github.com/Hawkynt/PNGCrushCS/issues/1178) |
| Alien Incident CDA2 | Container | `.cda` | demux + mux | missing | low | [#1179](https://github.com/Hawkynt/PNGCrushCS/issues/1179) |
| Amazing Studio Packed Animation File (PAF) | Container | `.paf` | demux + mux | missing | low | [#1180](https://github.com/Hawkynt/PNGCrushCS/issues/1180) |
| American Laser Games / Her Interactive AVF | Container | `.avf` | demux + mux | missing | low | [#1182](https://github.com/Hawkynt/PNGCrushCS/issues/1182) |
| American Laser Games MM | Container | `.mm` | demux + mux | missing | low | [#1183](https://github.com/Hawkynt/PNGCrushCS/issues/1183) |
| Animation Magic ANI | Container | `.ani` | demux + mux | missing | low | [#1185](https://github.com/Hawkynt/PNGCrushCS/issues/1185) |
| Apple Grandia Data (Sega Saturn Grandia) | Container | `.mov` | demux + mux | missing | low | [#1186](https://github.com/Hawkynt/PNGCrushCS/issues/1186) |
| Arc Developments HUF | Container | `.huf` | demux + mux | missing | low | [#1187](https://github.com/Hawkynt/PNGCrushCS/issues/1187) |
| Archimedean Dynasty (Schleichfahrt) MVI | Container | `.mvi` | demux + mux | missing | low | [#1188](https://github.com/Hawkynt/PNGCrushCS/issues/1188) |
| Argonaut Creature Shock AVS | Container | `.avs` | demux + mux | missing | low | [#1189](https://github.com/Hawkynt/PNGCrushCS/issues/1189) |
| Argonaut Games BRP | Container | `.brp` | demux + mux | missing | low | [#1190](https://github.com/Hawkynt/PNGCrushCS/issues/1190) |
| Ark of Time AN | Container | `.an` | demux + mux | missing | low | [#1192](https://github.com/Hawkynt/PNGCrushCS/issues/1192) |
| Arxel Tribe CNM / CI2 | Container | `.ci2`, `.cnm` | demux + mux | missing | low | [#1193](https://github.com/Hawkynt/PNGCrushCS/issues/1193) |
| Ascaron animation (Pole Position: Formel 1 Teamchef) | Container | `.anm` | demux + mux | missing | low | [#1194](https://github.com/Hawkynt/PNGCrushCS/issues/1194) |
| Ascon animation (Gloriana / Elisabeth I) | Container | `ascon` | demux + mux | missing | low | [#1195](https://github.com/Hawkynt/PNGCrushCS/issues/1195) |
| Ascon SKS (Patrizier II) | Container | `.sks` | demux + mux | missing | low | [#1196](https://github.com/Hawkynt/PNGCrushCS/issues/1196) |
| Baldies animation (BAL) | Container | `.bal` | demux + mux | missing | low | [#1197](https://github.com/Hawkynt/PNGCrushCS/issues/1197) |
| Beam Software SIFF (VB/SON) | Container | `.vb`, `.son`, `.fcp` | demux + mux | missing | low | [#1198](https://github.com/Hawkynt/PNGCrushCS/issues/1198) |
| Bethesda Softworks VID | Container | `.vid` | demux + mux | missing | low | [#1200](https://github.com/Hawkynt/PNGCrushCS/issues/1200) |
| Bitmap Brothers JV | Container | `.jv` | demux + mux | missing | low | [#1202](https://github.com/Hawkynt/PNGCrushCS/issues/1202) |
| Black Diamond STR (Psychic Detective) | Container | `.str` | demux + mux | missing | low | [#1204](https://github.com/Hawkynt/PNGCrushCS/issues/1204) |
| Brainstorm Technology DMV | Container | `.dmv` | demux + mux | missing | low | [#1205](https://github.com/Hawkynt/PNGCrushCS/issues/1205) |
| Caiman MXV (Yo, Matias) | Container | `.mxv` | demux + mux | missing | low | [#1207](https://github.com/Hawkynt/PNGCrushCS/issues/1207) |
| Castles II greyscale video (.M) | Container | `.m` | demux + mux | missing | low | [#1208](https://github.com/Hawkynt/PNGCrushCS/issues/1208) |
| CD+Graphics (CD+G) | Container | `.cdg` | demux + mux | missing | low | [#1209](https://github.com/Hawkynt/PNGCrushCS/issues/1209) |
| Celestial Impact DXV | Container | `.dxv` | demux + mux | missing | low | [#1211](https://github.com/Hawkynt/PNGCrushCS/issues/1211) |
| Chewy: Esc from F5 CFO | Container | `.cfo` | demux + mux | missing | low | [#1212](https://github.com/Hawkynt/PNGCrushCS/issues/1212) |
| Chronomaster DFA | Container | `.dfa` | demux + mux | blocked | low | [#1214](https://github.com/Hawkynt/PNGCrushCS/issues/1214) |
| Coktel Vision IMD | Container | `.imd` | demux + mux | missing | low | [#1216](https://github.com/Hawkynt/PNGCrushCS/issues/1216) |
| Companions of Xanth cutscenes (XANTH_9x.PIC) | Container | `xanth` | demux + mux | missing | low | [#1218](https://github.com/Hawkynt/PNGCrushCS/issues/1218) |
| CRI USM (Sofdec 2) | Container | `.usm` | demux + mux | missing | low | [#1221](https://github.com/Hawkynt/PNGCrushCS/issues/1221) |
| Crusade FPK | Container | `.fpk` | demux + mux | missing | low | [#1222](https://github.com/Hawkynt/PNGCrushCS/issues/1222) |
| Cryo HNM (versions 0 and 1) | Container | `.hnm` | demux + mux | missing | low | [#1223](https://github.com/Hawkynt/PNGCrushCS/issues/1223) |
| Cryo HNM4 | Container | `.hnm` | demux + mux | missing | low | [#1224](https://github.com/Hawkynt/PNGCrushCS/issues/1224) |
| Cryo HNM6 | Container | `.hnm`, `.hns` | demux + mux | missing | low | [#1226](https://github.com/Hawkynt/PNGCrushCS/issues/1226) |
| Cryo UBB2 (HNM5) | Container | `.ubb` | demux + mux | missing | low | [#1227](https://github.com/Hawkynt/PNGCrushCS/issues/1227) |
| Cyberflix DreamFactory (CFDF) movies | Container | `.move`, `.trk`, `.mov` | demux + mux | missing | low | [#1228](https://github.com/Hawkynt/PNGCrushCS/issues/1228) |
| Cyclemania video | Container | `.hed` | demux + mux | missing | low | [#1229](https://github.com/Hawkynt/PNGCrushCS/issues/1229) |
| Cydonia Paco | Container | `.pac` | demux + mux | missing | low | [#1230](https://github.com/Hawkynt/PNGCrushCS/issues/1230) |
| Dark Moon UM | Container | `.um` | demux + mux | missing | low | [#1231](https://github.com/Hawkynt/PNGCrushCS/issues/1231) |
| Data East Sega CD FMV (Road Avenger, Cobra Command, Revenge of the Ninja) | Container | `.bin` | demux + mux | missing | low | [#1232](https://github.com/Hawkynt/PNGCrushCS/issues/1232) |
| Death Rally HAF | Container | `.haf` | demux + mux | missing | low | [#1233](https://github.com/Hawkynt/PNGCrushCS/issues/1233) |
| Defcon 5 VID | Container | `defcon5` | demux + mux | missing | low | [#1234](https://github.com/Hawkynt/PNGCrushCS/issues/1234) |
| Delphine Software International CIN | Container | `.cin` | demux + mux | missing | low | [#1236](https://github.com/Hawkynt/PNGCrushCS/issues/1236) |
| Deluxe Paint Animation (LPF ANM) | Container | `.anm` | demux + mux | blocked | low | [#1238](https://github.com/Hawkynt/PNGCrushCS/issues/1238) |
| Digital Integration Q3 (Hind) | Container | `.q3` | demux + mux | missing | low | [#1239](https://github.com/Hawkynt/PNGCrushCS/issues/1239) |
| Digital Pictures SGA | Container | `.sga` | demux + mux | missing | low | [#1240](https://github.com/Hawkynt/PNGCrushCS/issues/1240) |
| Discworld II BMV | Container | `.bmv` | demux + mux | missing | low | [#1242](https://github.com/Hawkynt/PNGCrushCS/issues/1242) |
| Disney Animation Studio CFAST | Container | `.cft` | demux + mux | missing | low | [#1244](https://github.com/Hawkynt/PNGCrushCS/issues/1244) |
| Disney's Hercules ETV | Container | `.etv` | demux + mux | missing | low | [#1245](https://github.com/Hawkynt/PNGCrushCS/issues/1245) |
| Dorling Kindersley animation | Container | `.ani` | demux + mux | missing | low | [#1246](https://github.com/Hawkynt/PNGCrushCS/issues/1246) |
| DPEG (Bloodwings: Pumpkinhead's Revenge) | Container | `.dpg` | demux + mux | missing | low | [#1247](https://github.com/Hawkynt/PNGCrushCS/issues/1247) |
| Dwarf Fortress CMV / CCMV | Container | `.cmv`, `.ccmv` | demux + mux | missing | low | [#1248](https://github.com/Hawkynt/PNGCrushCS/issues/1248) |
| DXA (Feeble Files / ScummVM) | Container | `.dxa` | demux + mux | missing | low | [#1249](https://github.com/Hawkynt/PNGCrushCS/issues/1249) |
| Ecco the Dolphin LMS | Container | `.lms` | demux + mux | missing | low | [#1250](https://github.com/Hawkynt/PNGCrushCS/issues/1250) |
| Elige Tu Muerte FX | Container | `.mov` | demux + mux | missing | low | [#1257](https://github.com/Hawkynt/PNGCrushCS/issues/1257) |
| Entertainment Software Partners RL2 | Container | `.rl2` | demux + mux | missing | low | [#1258](https://github.com/Hawkynt/PNGCrushCS/issues/1258) |
| EVA animation (T. Yamamoto) | Container | `.eva` | demux + mux | missing | low | [#1259](https://github.com/Hawkynt/PNGCrushCS/issues/1259) |
| Event Horizons MOV (CGA) | Container | `.mov` | demux + mux | missing | low | [#1260](https://github.com/Hawkynt/PNGCrushCS/issues/1260) |
| Factor 5 VID1 | Container | `.vid` | demux + mux | missing | low | [#1261](https://github.com/Hawkynt/PNGCrushCS/issues/1261) |
| FLAC (FLI with sound, Bureau 13 / Hell trailers) | Container | `.av`, `.fla` | demux + mux | missing | low | [#1263](https://github.com/Hawkynt/PNGCrushCS/issues/1263) |
| Flashpoint Noctropolis VID | Container | `.vid` | demux + mux | missing | low | [#1264](https://github.com/Hawkynt/PNGCrushCS/issues/1264) |
| FLK (Selen) | Container | `.flk` | demux + mux | missing | low | [#1265](https://github.com/Hawkynt/PNGCrushCS/issues/1265) |
| FM Towns MVB | Container | `.mvb` | demux + mux | missing | low | [#1266](https://github.com/Hawkynt/PNGCrushCS/issues/1266) |
| Frankenstein BFC (Sega CD) | Container | `.bfc` | demux + mux | missing | low | [#1267](https://github.com/Hawkynt/PNGCrushCS/issues/1267) |
| FutureVision FST (Harvester) | Container | `.fst` | demux + mux | missing | low | [#1268](https://github.com/Hawkynt/PNGCrushCS/issues/1268) |
| Game-specific FLIC variants (Alien Virus ANI, Bureau 13, Hammer of the Gods C13, Stargunner PGBP, Origin FORM/FLIC, Conquest Earth FLH) | Container | `.ani`, `.c13`, `.flc`, `.dat`, `.flh` | demux + mux | missing | low | [#1269](https://github.com/Hawkynt/PNGCrushCS/issues/1269) |
| Gates of Skeldal MGIF | Container | `.mgf` | demux + mux | missing | low | [#1270](https://github.com/Hawkynt/PNGCrushCS/issues/1270) |
| Golem intro XFL | Container | `.xfl` | demux + mux | missing | low | [#1271](https://github.com/Hawkynt/PNGCrushCS/issues/1271) |
| Gremlin Digital Video | Container | `.gdv` | demux + mux | missing | low | [#1273](https://github.com/Hawkynt/PNGCrushCS/issues/1273) |
| GTE Vantage animation (GAV) | Container | `.gav` | demux + mux | missing | low | [#1275](https://github.com/Hawkynt/PNGCrushCS/issues/1275) |
| Gunmetal SBV | Container | `.sbv` | demux + mux | missing | low | [#1276](https://github.com/Hawkynt/PNGCrushCS/issues/1276) |
| Guts'n'Garters animation (.15 / .ani) | Container | `.15`, `.ani` | demux + mux | missing | low | [#1277](https://github.com/Hawkynt/PNGCrushCS/issues/1277) |
| Harrow Media VDO (The Fuel Run) | Container | `.vdo` | demux + mux | missing | low | [#1278](https://github.com/Hawkynt/PNGCrushCS/issues/1278) |
| Highlander FMV | Container | `.fmv` | demux + mux | missing | low | [#1279](https://github.com/Hawkynt/PNGCrushCS/issues/1279) |
| Hudson HVQM2/HVQM3/HVQM4 (H4M) | Container | `.h4m`, `.hvqm` | demux + mux | missing | low | [#1280](https://github.com/Hawkynt/PNGCrushCS/issues/1280) |
| Humongous Entertainment CUP | Container | `.cup` | demux + mux | missing | low | [#1281](https://github.com/Hawkynt/PNGCrushCS/issues/1281) |
| ICOM video (Sherlock Holmes Consulting Detective) | Container | `.imv` | demux + mux | missing | low | [#1282](https://github.com/Hawkynt/PNGCrushCS/issues/1282) |
| Ikarion MVI1 / MVI2 | Container | `.mvi` | demux + mux | missing | low | [#1284](https://github.com/Hawkynt/PNGCrushCS/issues/1284) |
| ILDA Image Data Transfer Format | Container | `.ild` | demux + mux | missing | low | [#1285](https://github.com/Hawkynt/PNGCrushCS/issues/1285) |
| Independence Day EGG / YQS | Container | `.egg`, `.yqs` | demux + mux | missing | low | [#1287](https://github.com/Hawkynt/PNGCrushCS/issues/1287) |
| Infocom/Activision PMV (Return to Zork) | Container | `.pmv`, `.mmv`, `.lmv` | demux + mux | missing | low | [#1288](https://github.com/Hawkynt/PNGCrushCS/issues/1288) |
| Infogrames MUX | Container | `.mux` | demux + mux | missing | low | [#1289](https://github.com/Hawkynt/PNGCrushCS/issues/1289) |
| Intelligent Games MOV (Azrael's Tear) | Container | `.mov` | demux + mux | missing | low | [#1290](https://github.com/Hawkynt/PNGCrushCS/issues/1290) |
| Interactive Pictures VID | Container | `.evd`, `.fvd`, `.gvd` | demux + mux | missing | low | [#1291](https://github.com/Hawkynt/PNGCrushCS/issues/1291) |
| Interplay C93 (Cyberia) | Container | `.c93` | demux + mux | missing | low | [#1293](https://github.com/Hawkynt/PNGCrushCS/issues/1293) |
| Interplay M95 (Cyberia 2) | Container | `.m95` | demux + mux | missing | low | [#1294](https://github.com/Hawkynt/PNGCrushCS/issues/1294) |
| Interspective cutscenes (Innocent Until Caught / Guilty) | Container | `guilty` | demux + mux | missing | low | [#1295](https://github.com/Hawkynt/PNGCrushCS/issues/1295) |
| Jack Orlando AVX | Container | `.avx` | demux + mux | missing | low | [#1296](https://github.com/Hawkynt/PNGCrushCS/issues/1296) |
| JAM video | Container | `.jam` | demux + mux | missing | low | [#1297](https://github.com/Hawkynt/PNGCrushCS/issues/1297) |
| Jazz Jackrabbit 2 cutscene (J2V) | Container | `.j2v` | demux + mux | missing | low | [#1298](https://github.com/Hawkynt/PNGCrushCS/issues/1298) |
| Kalisto CRH (Dark Earth, Nightmare Creatures) | Container | `crh-de`, `crh-nc` | demux + mux | missing | low | [#1299](https://github.com/Hawkynt/PNGCrushCS/issues/1299) |
| Kheops Studio Video (KSV) | Container | `.ksv` | demux + mux | missing | low | [#1302](https://github.com/Hawkynt/PNGCrushCS/issues/1302) |
| Klondike Moon VID/SEQ/SFX | Container | `.vid` | demux + mux | missing | low | [#1303](https://github.com/Hawkynt/PNGCrushCS/issues/1303) |
| Knowledge Adventure MOV v2/v3 (Space Adventure) | Container | `.mov`, `.mv0`, `.mv1`, `.mv3`, `.mv4`, `.mv5` | demux + mux | missing | low | [#1304](https://github.com/Hawkynt/PNGCrushCS/issues/1304) |
| Lantern Movie v2 (IFF) / v3 (RIFF) | Container | `.mov` | demux + mux | missing | low | [#1305](https://github.com/Hawkynt/PNGCrushCS/issues/1305) |
| Legend Entertainment Q | Container | `.q` | demux + mux | missing | low | [#1306](https://github.com/Hawkynt/PNGCrushCS/issues/1306) |
| Little Big Adventure FLA | Container | `.fla` | demux + mux | missing | low | [#1307](https://github.com/Hawkynt/PNGCrushCS/issues/1307) |
| Loki SMJPEG | Container | `.mjpg` | demux + mux | missing | low | [#1308](https://github.com/Hawkynt/PNGCrushCS/issues/1308) |
| Lord of the Rings AV | Container | `.av` | demux + mux | missing | low | [#1309](https://github.com/Hawkynt/PNGCrushCS/issues/1309) |
| Lords of the Realm VAS | Container | `.vas` | demux + mux | missing | low | [#1310](https://github.com/Hawkynt/PNGCrushCS/issues/1310) |
| LucasArts Dark Forces LFD film (ANIM/FILM resources) | Container | `.lfd` | demux + mux | missing | low | [#1311](https://github.com/Hawkynt/PNGCrushCS/issues/1311) |
| LucasArts SMUSH (SAN/SNM) | Container | `.san`, `.snm`, `.nut` | demux + mux | missing | low | [#1313](https://github.com/Hawkynt/PNGCrushCS/issues/1313) |
| LZANIM (LZA) | Container | `.lza` | demux + mux | missing | low | [#1314](https://github.com/Hawkynt/PNGCrushCS/issues/1314) |
| Machine Hunter / Eurocom FMV | Container | `.fmv` | demux + mux | missing | low | [#1315](https://github.com/Hawkynt/PNGCrushCS/issues/1315) |
| Maelstrom ANM | Container | `.anm` | demux + mux | missing | low | [#1316](https://github.com/Hawkynt/PNGCrushCS/issues/1316) |
| Magic Lantern DIFF (Amiga) | Container | `.diff` | demux + mux | missing | low | [#1317](https://github.com/Hawkynt/PNGCrushCS/issues/1317) |
| Makh-Shevet SMA | Container | `.sma` | demux + mux | missing | low | [#1318](https://github.com/Hawkynt/PNGCrushCS/issues/1318) |
| Manic Karts AMF | Container | `.amf` | demux + mux | missing | low | [#1319](https://github.com/Hawkynt/PNGCrushCS/issues/1319) |
| Metal Gear Solid: The Twin Snakes VP3 container | Container | `MGS VP3` | demux + mux | missing | low | [#1320](https://github.com/Hawkynt/PNGCrushCS/issues/1320) |
| Microcosm CA2 | Container | `.ca2` | demux + mux | missing | low | [#1321](https://github.com/Hawkynt/PNGCrushCS/issues/1321) |
| Microids GRN | Container | `.grn` | demux + mux | missing | low | [#1322](https://github.com/Hawkynt/PNGCrushCS/issues/1322) |
| Microsoft Xbox XMV | Container | `.xmv` | demux + mux | missing | low | [#1323](https://github.com/Hawkynt/PNGCrushCS/issues/1323) |
| Midnight Raiders JOE/CMD (Sega CD) | Container | `.joe`, `.cmd` | demux + mux | missing | low | [#1324](https://github.com/Hawkynt/PNGCrushCS/issues/1324) |
| Motion Pixels MVI | Container | `.mvi` | demux + mux | missing | low | [#1326](https://github.com/Hawkynt/PNGCrushCS/issues/1326) |
| Moving Puzzles MV | Container | `.mv` | demux + mux | missing | low | [#1327](https://github.com/Hawkynt/PNGCrushCS/issues/1327) |
| MP Entertainment animation (Hopkins FBI) | Container | `.anm`, `.seq` | demux + mux | missing | low | [#1328](https://github.com/Hawkynt/PNGCrushCS/issues/1328) |
| Murder FILM (Amiga) | Container | `.film` | demux + mux | missing | low | [#1329](https://github.com/Hawkynt/PNGCrushCS/issues/1329) |
| NEC HsMovie | Container | `.mov` | demux + mux | missing | low | [#1330](https://github.com/Hawkynt/PNGCrushCS/issues/1330) |
| Network Q RAC Rally FMV | Container | `qrac-fmv` | demux + mux | missing | low | [#1331](https://github.com/Hawkynt/PNGCrushCS/issues/1331) |
| Nightlong FMV / FMV2 (Amiga) | Container | `.fmv2` | demux + mux | missing | low | [#1332](https://github.com/Hawkynt/PNGCrushCS/issues/1332) |
| Nikita NAV | Container | `.nav` | demux + mux | missing | low | [#1333](https://github.com/Hawkynt/PNGCrushCS/issues/1333) |
| Ninell Animation File (NAF) | Container | `naf` | demux + mux | missing | low | [#1334](https://github.com/Hawkynt/PNGCrushCS/issues/1334) |
| Nintendo THP | Container | `.thp` | demux + mux | missing | low | [#1336](https://github.com/Hawkynt/PNGCrushCS/issues/1336) |
| Nova Logic KDV | Container | `.kdv` | demux + mux | missing | low | [#1337](https://github.com/Hawkynt/PNGCrushCS/issues/1337) |
| Optonica Videostream VAXL | Container | `.vaxl` | demux + mux | missing | low | [#1338](https://github.com/Hawkynt/PNGCrushCS/issues/1338) |
| Origin Wing Commander III MVE | Container | `.mve` | demux + mux | missing | low | [#1340](https://github.com/Hawkynt/PNGCrushCS/issues/1340) |
| PACo video (Iron Helix, Hell Cab) | Container | `paco` | demux + mux | missing | low | [#1341](https://github.com/Hawkynt/PNGCrushCS/issues/1341) |
| PC Animate Plus | Container | `.ani` | demux + mux | missing | low | [#1342](https://github.com/Hawkynt/PNGCrushCS/issues/1342) |
| PC-Animation (Hugo) | Container | `pc-anim` | demux + mux | missing | low | [#1343](https://github.com/Hawkynt/PNGCrushCS/issues/1343) |
| PlayStation 2 IPU video | Container | `.ipu` | demux + mux | missing | low | [#1348](https://github.com/Hawkynt/PNGCrushCS/issues/1348) |
| PlayStation Portable Movie Format (PSMF / PMF) | Container | `.pmf` | demux + mux | missing | low | [#1349](https://github.com/Hawkynt/PNGCrushCS/issues/1349) |
| Powerslave LMF (book.mov) | Container | `.mov` | demux + mux | missing | low | [#1350](https://github.com/Hawkynt/PNGCrushCS/issues/1350) |
| Pray for Death CDA | Container | `.cda` | demux + mux | missing | low | [#1351](https://github.com/Hawkynt/PNGCrushCS/issues/1351) |
| Psygnosis Bram Stoker's Dracula VID (Sega CD) | Container | `.vid` | demux + mux | missing | low | [#1352](https://github.com/Hawkynt/PNGCrushCS/issues/1352) |
| Psygnosis MultiMedia (PMM) | Container | `.pmm` | demux + mux | missing | low | [#1353](https://github.com/Hawkynt/PNGCrushCS/issues/1353) |
| Psygnosis Novastorm media (.fa/.flm) | Container | `.fa`, `.flm` | demux + mux | missing | low | [#1354](https://github.com/Hawkynt/PNGCrushCS/issues/1354) |
| Psygnosis Reaper FMV (Rollcage) | Container | `.fmv` | demux + mux | missing | low | [#1355](https://github.com/Hawkynt/PNGCrushCS/issues/1355) |
| Psygnosis SMV (WipEout) | Container | `.smv` | demux + mux | missing | low | [#1356](https://github.com/Hawkynt/PNGCrushCS/issues/1356) |
| Psygnosis YOP | Container | `.yop` | demux + mux | missing | low | [#1357](https://github.com/Hawkynt/PNGCrushCS/issues/1357) |
| Q-Team DVC animation | Container | `.dvc` | demux + mux | missing | low | [#1359](https://github.com/Hawkynt/PNGCrushCS/issues/1359) |
| Rainbow Technology F16 | Container | `.f16` | demux + mux | missing | low | [#1360](https://github.com/Hawkynt/PNGCrushCS/issues/1360) |
| Ravenloft ANM | Container | `.anm` | demux + mux | missing | low | [#1361](https://github.com/Hawkynt/PNGCrushCS/issues/1361) |
| ReadySoft PC-port scenes (Dragon's Lair, Space Ace, Brain Dead 13) | Container | `.dat` | demux + mux | missing | low | [#1362](https://github.com/Hawkynt/PNGCrushCS/issues/1362) |
| Reflections animation | Container | `.ani` | demux + mux | missing | low | [#1363](https://github.com/Hawkynt/PNGCrushCS/issues/1363) |
| reLINE VPX1 (BIING!) | Container | `.vpx` | demux + mux | missing | low | [#1364](https://github.com/Hawkynt/PNGCrushCS/issues/1364) |
| Roketz VQM | Container | `.vqm` | demux + mux | missing | low | [#1366](https://github.com/Hawkynt/PNGCrushCS/issues/1366) |
| Savage Warriors ANM | Container | `.anm` | demux + mux | missing | low | [#1367](https://github.com/Hawkynt/PNGCrushCS/issues/1367) |
| Sega FILM / CPK | Container | `.cpk`, `.cak`, `.film` | demux + mux | missing | low | [#1368](https://github.com/Hawkynt/PNGCrushCS/issues/1368) |
| Sesame Street STR | Container | `.str` | demux + mux | missing | low | [#1369](https://github.com/Hawkynt/PNGCrushCS/issues/1369) |
| SGI OOB MV | Container | `oob-mv` | demux + mux | missing | low | [#1370](https://github.com/Hawkynt/PNGCrushCS/issues/1370) |
| Shadowcaster (CD) cutscenes | Container | `.dat` | demux + mux | missing | low | [#1371](https://github.com/Hawkynt/PNGCrushCS/issues/1371) |
| Sierra Robot animation (RBT) | Container | `.rbt` | demux + mux | missing | low | [#1372](https://github.com/Hawkynt/PNGCrushCS/issues/1372) |
| Simbiosis Interactive IMX (Fable) | Container | `.imx` | demux + mux | missing | low | [#1374](https://github.com/Hawkynt/PNGCrushCS/issues/1374) |
| Sonic CD video (STM) | Container | `.stm` | demux + mux | missing | low | [#1376](https://github.com/Hawkynt/PNGCrushCS/issues/1376) |
| Sony PlayStation Advanced Movie Format (PAMF) | Container | `.pam`, `.pamf` | demux + mux | missing | low | [#1377](https://github.com/Hawkynt/PNGCrushCS/issues/1377) |
| Soul Hunt VID | Container | `.vid` | demux + mux | missing | low | [#1378](https://github.com/Hawkynt/PNGCrushCS/issues/1378) |
| Spectrum Holobyte FVF | Container | `.fvf` | demux + mux | missing | low | [#1379](https://github.com/Hawkynt/PNGCrushCS/issues/1379) |
| Spidy animation v2 (Imperium Galactica) | Container | `.ani` | demux + mux | missing | low | [#1380](https://github.com/Hawkynt/PNGCrushCS/issues/1380) |
| Star Control II 3DO TrueMotion video (.duk/.hdr/.frm/.tbl) | Container | `.duk` | demux + mux | blocked | low | [#1381](https://github.com/Hawkynt/PNGCrushCS/issues/1381) |
| Stardock VDO (Entrepreneur) | Container | `.vdo` | demux + mux | missing | low | [#1382](https://github.com/Hawkynt/PNGCrushCS/issues/1382) |
| System Shock movie/audio log format | Container | `.movie` | demux + mux | missing | low | [#1383](https://github.com/Hawkynt/PNGCrushCS/issues/1383) |
| Talisman ANI | Container | `.ani` | demux + mux | missing | low | [#1384](https://github.com/Hawkynt/PNGCrushCS/issues/1384) |
| Tandyvision video | Container | `tandyvid` | demux + mux | missing | low | [#1385](https://github.com/Hawkynt/PNGCrushCS/issues/1385) |
| Terminal Velocity TVI | Container | `.tvi` | demux + mux | missing | low | [#1386](https://github.com/Hawkynt/PNGCrushCS/issues/1386) |
| The Amazing Spider-Man vs The Kingpin cutscene (Sega CD) | Container | `kingpin-bin` | demux + mux | missing | low | [#1387](https://github.com/Hawkynt/PNGCrushCS/issues/1387) |
| The Koshan Conspiracy AND | Container | `.and` | demux + mux | missing | low | [#1388](https://github.com/Hawkynt/PNGCrushCS/issues/1388) |
| The Lawnmower Man NXL | Container | `.nxl` | demux + mux | missing | low | [#1389](https://github.com/Hawkynt/PNGCrushCS/issues/1389) |
| The Pandora Directive H2O | Container | `.h2o` | demux + mux | missing | low | [#1390](https://github.com/Hawkynt/PNGCrushCS/issues/1390) |
| Theatrix WMO (Big Science Comics) | Container | `.wmo` | demux + mux | missing | low | [#1391](https://github.com/Hawkynt/PNGCrushCS/issues/1391) |
| Thunder in Paradise DRL | Container | `.drl` | demux + mux | missing | low | [#1392](https://github.com/Hawkynt/PNGCrushCS/issues/1392) |
| Tie Break Tennis 98 intro | Container | `.anm` | demux + mux | missing | low | [#1393](https://github.com/Hawkynt/PNGCrushCS/issues/1393) |
| Tiertex Limited SEQ | Container | `.seq` | demux + mux | missing | low | [#1394](https://github.com/Hawkynt/PNGCrushCS/issues/1394) |
| TMM fractal video | Container | `tmm-frac` | demux + mux | missing | low | [#1396](https://github.com/Hawkynt/PNGCrushCS/issues/1396) |
| Total Multimedia PH video | Container | `.ph` | demux + mux | missing | low | [#1397](https://github.com/Hawkynt/PNGCrushCS/issues/1397) |
| Traveller's Tales FMV | Container | `.fmv` | demux + mux | missing | low | [#1398](https://github.com/Hawkynt/PNGCrushCS/issues/1398) |
| Trilobyte VDX (The 7th Guest) | Container | `.vdx`, `.gjd` | demux + mux | missing | low | [#1399](https://github.com/Hawkynt/PNGCrushCS/issues/1399) |
| Tsunami Media animation (IVAS) | Container | `.image` | demux + mux | missing | low | [#1400](https://github.com/Hawkynt/PNGCrushCS/issues/1400) |
| UMV (Are You Afraid of the Dark / Dracula Unleashed) | Container | `umv-afraid`, `umv-dracula` | demux + mux | missing | low | [#1401](https://github.com/Hawkynt/PNGCrushCS/issues/1401) |
| Under a Killing Moon PTF | Container | `.ptf` | demux + mux | missing | low | [#1402](https://github.com/Hawkynt/PNGCrushCS/issues/1402) |
| Video Toaster RTV | Container | `.rtv` | demux + mux | missing | low | [#1403](https://github.com/Hawkynt/PNGCrushCS/issues/1403) |
| VIDPAK / Ratvideo | Container | `.vdo` | demux + mux | missing | low | [#1404](https://github.com/Hawkynt/PNGCrushCS/issues/1404) |
| VTech V.Flash MJP movie (PTX) | Container | `.ptx` | demux + mux | missing | low | [#1405](https://github.com/Hawkynt/PNGCrushCS/issues/1405) |
| Whale's Voyage II LSD | Container | `.lsd` | demux + mux | missing | low | [#1406](https://github.com/Hawkynt/PNGCrushCS/issues/1406) |
| Wing Nuts / Lodestar scenes | Container | `.dat` | demux + mux | missing | low | [#1409](https://github.com/Hawkynt/PNGCrushCS/issues/1409) |
| World Fairy Tales IBM | Container | `.tj`, `.tj_` | demux + mux | missing | low | [#1410](https://github.com/Hawkynt/PNGCrushCS/issues/1410) |
| XBV (Call of Duty 3 Xbox) | Container | `.xbv` | demux + mux | missing | low | [#1411](https://github.com/Hawkynt/PNGCrushCS/issues/1411) |
| Xilam DERF video (Stupid Invaders) | Container | `.vds` | demux + mux | missing | low | [#1412](https://github.com/Hawkynt/PNGCrushCS/issues/1412) |
| Yumimi Mix animations (ABLK/ACUT) | Container | `.abk`, `.dat`, `.cut` | demux + mux | missing | low | [#1413](https://github.com/Hawkynt/PNGCrushCS/issues/1413) |
| Zork RLF | Container | `.rlf` | demux + mux | missing | low | [#1414](https://github.com/Hawkynt/PNGCrushCS/issues/1414) |
| Zorton Brothers / Los Justicieros VIP | Container | `.vip` | demux + mux | missing | low | [#1415](https://github.com/Hawkynt/PNGCrushCS/issues/1415) |
| Brute Force & Ignorance Video encoder | Encoder | `bfi` | encode | decoder only | low | [#1206](https://github.com/Hawkynt/PNGCrushCS/issues/1206) |
| Commodore CDXL Video encoder | Encoder | `cdxl` | encode | decoder only | low | [#1217](https://github.com/Hawkynt/PNGCrushCS/issues/1217) |
| Eidos Escape 130 encoder | Encoder | `ESCP 130` | encode | decoder only | low | [#1252](https://github.com/Hawkynt/PNGCrushCS/issues/1252) |
| id Cinematic Video (Quake II CIN) encoder | Encoder | `idcin` | encode | decoder only | low | [#1283](https://github.com/Hawkynt/PNGCrushCS/issues/1283) |
| Sierra VMD Video encoder | Encoder | `vmdvideo` | encode | decoder only | low | [#1373](https://github.com/Hawkynt/PNGCrushCS/issues/1373) |

## Legacy PC & capture-card video

1990s-2000s AVI/QuickTime codecs, capture cards, webcams, surveillance, portable players. Tracked in [#525](https://github.com/Hawkynt/PNGCrushCS/issues/525).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| On2 VP6 (VP60/VP61/VP62, Flash VP6F, VP6A alpha) | Codec | `.flv`, `.avi`, `.vp6`, `.nsv` | decode + encode | blocked | high | [#1416](https://github.com/Hawkynt/PNGCrushCS/issues/1416) |
| AMV video (MTV/AMV player) | Codec | `.amv`, `.mtv` | decode + encode | missing | medium | [#1418](https://github.com/Hawkynt/PNGCrushCS/issues/1418) |
| On2 VP7 | Codec | `.vp7`, `.avi` | decode + encode | blocked | medium | [#1419](https://github.com/Hawkynt/PNGCrushCS/issues/1419) |
| Sorenson Video 1 (SVQ1) | Codec | `.mov` | decode + encode | blocked | medium | [#1421](https://github.com/Hawkynt/PNGCrushCS/issues/1421) |
| Sorenson Video 3 (SVQ3) | Codec | `.mov` | decode + encode | blocked | medium | [#1422](https://github.com/Hawkynt/PNGCrushCS/issues/1422) |
| AMV (Chinese MP4/MTV player video) | Container | `.amv` | demux + mux | missing | medium | [#1417](https://github.com/Hawkynt/PNGCrushCS/issues/1417) |
| Shockwave Flash video streams (SWF) | Container | `.swf` | demux + mux | missing | medium | [#1420](https://github.com/Hawkynt/PNGCrushCS/issues/1420) |
| AJPG camera Motion JPEG | Codec | `AJPG` | decode + encode | missing | low | [#1423](https://github.com/Hawkynt/PNGCrushCS/issues/1423) |
| ATI VCR1 | Codec | `.avi` | decode + encode | missing | low | [#1424](https://github.com/Hawkynt/PNGCrushCS/issues/1424) |
| Auravision AURA | Codec | `aura`, `AURA` | decode + encode | missing | low | [#1425](https://github.com/Hawkynt/PNGCrushCS/issues/1425) |
| Auravision Aura 2 | Codec | `aura2`, `AUR2` | decode + encode | missing | low | [#1426](https://github.com/Hawkynt/PNGCrushCS/issues/1426) |
| Aware Motion Wavelets (MWV1) | Codec | `MWV1` | decode + encode | missing | low | [#1427](https://github.com/Hawkynt/PNGCrushCS/issues/1427) |
| Brooktree ProSumer Video (BT20) | Codec | `.avi` | decode + encode | blocked | low | [#1428](https://github.com/Hawkynt/PNGCrushCS/issues/1428) |
| Caimans Video Codec for GBA | Codec | `Caimans` | decode + encode | missing | low | [#1429](https://github.com/Hawkynt/PNGCrushCS/issues/1429) |
| CPiA webcam video | Codec | `cpia` | decode + encode | missing | low | [#1430](https://github.com/Hawkynt/PNGCrushCS/issues/1430) |
| DPS Perception (CFCC) | Codec | `CFCC` | decode + encode | missing | low | [#1433](https://github.com/Hawkynt/PNGCrushCS/issues/1433) |
| Duck TrueMotion 2.0 Real Time (TR20) | Codec | `.avi` | decode + encode | missing | low | [#1435](https://github.com/Hawkynt/PNGCrushCS/issues/1435) |
| Euclid Video | Codec | `Euclid` | decode + encode | missing | low | [#1437](https://github.com/Hawkynt/PNGCrushCS/issues/1437) |
| Eyestream 7 (ES07) | Codec | `ES07` | decode + encode | missing | low | [#1438](https://github.com/Hawkynt/PNGCrushCS/issues/1438) |
| IBM UltiMotion | Codec | `.avi` | decode + encode | missing | low | [#1440](https://github.com/Hawkynt/PNGCrushCS/issues/1440) |
| ImagePower Motion JPEG 2000 (IPJ2) | Codec | `IPJ2` | decode + encode | missing | low | [#1442](https://github.com/Hawkynt/PNGCrushCS/issues/1442) |
| Infinity IMM4 | Codec | `.avi` | decode + encode | missing | low | [#1444](https://github.com/Hawkynt/PNGCrushCS/issues/1444) |
| Infinity IMM5 | Codec | `.avi` | decode + encode | missing | low | [#1445](https://github.com/Hawkynt/PNGCrushCS/issues/1445) |
| Intel H.263 (I263) | Codec | `.avi` | decode + encode | missing | low | [#1447](https://github.com/Hawkynt/PNGCrushCS/issues/1447) |
| Iterated Systems ClearVideo | Codec | `.avi`, `.mov` | decode + encode | missing | low | [#1448](https://github.com/Hawkynt/PNGCrushCS/issues/1448) |
| LEAD MCMP | Codec | `.avi` | decode + encode | missing | low | [#1450](https://github.com/Hawkynt/PNGCrushCS/issues/1450) |
| LEAD MCMW wavelet (LCW2) | Codec | `LCW2` | decode + encode | missing | low | [#1451](https://github.com/Hawkynt/PNGCrushCS/issues/1451) |
| Matrox MPEG-2 I-frame-only elementary (MMIF) | Codec | `MMIF` | decode + encode | missing | low | [#1454](https://github.com/Hawkynt/PNGCrushCS/issues/1454) |
| MidiVid 3.0 | Codec | `.avi` | decode + encode | missing | low | [#1457](https://github.com/Hawkynt/PNGCrushCS/issues/1457) |
| MidiVid VQ (MVDV) | Codec | `.avi` | decode + encode | missing | low | [#1458](https://github.com/Hawkynt/PNGCrushCS/issues/1458) |
| Mimic (MSN Messenger webcam) | Codec | `.cam` | decode + encode | missing | low | [#1460](https://github.com/Hawkynt/PNGCrushCS/issues/1460) |
| Miro VideoXL (VIXL) | Codec | `.avi` | decode + encode | missing | low | [#1461](https://github.com/Hawkynt/PNGCrushCS/issues/1461) |
| Mobotix MxPEG | Codec | `.mxg` | decode + encode | missing | low | [#1464](https://github.com/Hawkynt/PNGCrushCS/issues/1464) |
| Motion Pixels Video | Codec | `.mvi`, `.avi` | decode + encode | missing | low | [#1467](https://github.com/Hawkynt/PNGCrushCS/issues/1467) |
| MSU Lossless Video Codec | Codec | `MSUD` | decode + encode | missing | low | [#1469](https://github.com/Hawkynt/PNGCrushCS/issues/1469) |
| NuppelVideo RTJPEG | Codec | `.nuv` | decode + encode | missing | low | [#1474](https://github.com/Hawkynt/PNGCrushCS/issues/1474) |
| On2 VP4 | Codec | `.avi` | decode + encode | blocked | low | [#1476](https://github.com/Hawkynt/PNGCrushCS/issues/1476) |
| Pegasus Motion Wavelet 2000 (PVW2) | Codec | `PVW2` | decode + encode | missing | low | [#1479](https://github.com/Hawkynt/PNGCrushCS/issues/1479) |
| Radius Studio Video (PGVV) | Codec | `PGVV` | decode + encode | missing | low | [#1482](https://github.com/Hawkynt/PNGCrushCS/issues/1482) |
| SGI RLE 8-bit (rle1) | Codec | `.mv`, `.mov`, `.qt` | decode + encode | missing | low | [#1485](https://github.com/Hawkynt/PNGCrushCS/issues/1485) |
| Sigmatel Motion Video (SMV JPEG) | Codec | `.smv` | decode + encode | missing | low | [#1486](https://github.com/Hawkynt/PNGCrushCS/issues/1486) |
| Silicon Graphics Motion Video Compressor 1 | Codec | `.mv`, `.movie` | decode + encode | missing | low | [#1487](https://github.com/Hawkynt/PNGCrushCS/issues/1487) |
| Silicon Graphics Motion Video Compressor 2 | Codec | `.mv`, `.movie` | decode + encode | missing | low | [#1488](https://github.com/Hawkynt/PNGCrushCS/issues/1488) |
| Softimage AVI Reference Codec (SIRF) | Codec | `SIRF` | decode + encode | missing | low | [#1490](https://github.com/Hawkynt/PNGCrushCS/issues/1490) |
| Strathclyde Compression Transform (SCT4) | Codec | `SCT4` | decode + encode | missing | low | [#1491](https://github.com/Hawkynt/PNGCrushCS/issues/1491) |
| Sunplus JPEG (SP5X) | Codec | `.avi` | decode + encode | missing | low | [#1492](https://github.com/Hawkynt/PNGCrushCS/issues/1492) |
| VDOWave (VDOM/VDOW) | Codec | `VDOM`, `VDOW` | decode + encode | missing | low | [#1495](https://github.com/Hawkynt/PNGCrushCS/issues/1495) |
| VGM Video / VideoGramPiX (VGMV, VTLP, VGPX) | Codec | `VGMV`, `VTLP`, `VGPX` | decode + encode | missing | low | [#1497](https://github.com/Hawkynt/PNGCrushCS/issues/1497) |
| Vianet LSVX / LSVM | Codec | `LSVX`, `LSVM` | decode + encode | missing | low | [#1498](https://github.com/Hawkynt/PNGCrushCS/issues/1498) |
| ViewQuest VQC | Codec | `.avi` | decode + encode | missing | low | [#1500](https://github.com/Hawkynt/PNGCrushCS/issues/1500) |
| ViVD | Codec | `ViVD` | decode + encode | missing | low | [#1501](https://github.com/Hawkynt/PNGCrushCS/issues/1501) |
| VSS Codec Light (VSSV) | Codec | `VSSV` | decode + encode | missing | low | [#1503](https://github.com/Hawkynt/PNGCrushCS/issues/1503) |
| VSS Wavelet Video Codec (VSSW) | Codec | `VSSW` | decode + encode | missing | low | [#1504](https://github.com/Hawkynt/PNGCrushCS/issues/1504) |
| Winnov WINX | Codec | `WINX` | decode + encode | missing | low | [#1506](https://github.com/Hawkynt/PNGCrushCS/issues/1506) |
| Winnov WNV1 | Codec | `.avi` | decode + encode | missing | low | [#1507](https://github.com/Hawkynt/PNGCrushCS/issues/1507) |
| Xing MPEG I-frame codec (XMPG) | Codec | `XMPG`, `xmpg` | decode + encode | missing | low | [#1508](https://github.com/Hawkynt/PNGCrushCS/issues/1508) |
| Creative ZEN CMV | Container | `.cmv` | demux + mux | missing | low | [#1431](https://github.com/Hawkynt/PNGCrushCS/issues/1431) |
| Dahua DAV (DHAV) recording | Container | `.dav` | demux + mux | missing | low | [#1432](https://github.com/Hawkynt/PNGCrushCS/issues/1432) |
| Emblaze (Geo Interactive) EV2 | Container | `.ev2` | demux + mux | missing | low | [#1436](https://github.com/Hawkynt/PNGCrushCS/issues/1436) |
| HXVS/HXVF IP camera recording | Container | `.264`, `.265` | demux + mux | missing | low | [#1439](https://github.com/Hawkynt/PNGCrushCS/issues/1439) |
| IFV CCTV DVR | Container | `.ifv` | demux + mux | missing | low | [#1441](https://github.com/Hawkynt/PNGCrushCS/issues/1441) |
| IndigoVision 8000 video | Container | `.iv8` | demux + mux | missing | low | [#1443](https://github.com/Hawkynt/PNGCrushCS/issues/1443) |
| Ingenient raw MJPEG | Container | `.cgi` | demux + mux | missing | low | [#1446](https://github.com/Hawkynt/PNGCrushCS/issues/1446) |
| KUX (Youku) | Container | `.kux` | demux + mux | missing | low | [#1449](https://github.com/Hawkynt/PNGCrushCS/issues/1449) |
| Linux Media Labs MPEG-4 (LMLM4) | Container | `.lmlm4` | demux + mux | missing | low | [#1452](https://github.com/Hawkynt/PNGCrushCS/issues/1452) |
| LVF | Container | `.lvf` | demux + mux | missing | low | [#1453](https://github.com/Hawkynt/PNGCrushCS/issues/1453) |
| Microsoft Digital Video Recording (DVR-MS) | Container | `.dvr-ms` | demux + mux | missing | low | [#1456](https://github.com/Hawkynt/PNGCrushCS/issues/1456) |
| MIME multipart JPEG (MJPEG over HTTP) | Container | `.mjpg`, `.mpjpeg` | demux + mux | missing | low | [#1459](https://github.com/Hawkynt/PNGCrushCS/issues/1459) |
| Mobiclip MODS (Nintendo DS) | Container | `.mods` | demux + mux | missing | low | [#1462](https://github.com/Hawkynt/PNGCrushCS/issues/1462) |
| Mobiclip MOFLEX (Nintendo 3DS) | Container | `.moflex` | demux + mux | missing | low | [#1463](https://github.com/Hawkynt/PNGCrushCS/issues/1463) |
| Mobotix MxPEG clip | Container | `.mxg` | demux + mux | missing | low | [#1465](https://github.com/Hawkynt/PNGCrushCS/issues/1465) |
| Moonshell DPG (Nintendo DS) | Container | `.dpg` | demux + mux | missing | low | [#1466](https://github.com/Hawkynt/PNGCrushCS/issues/1466) |
| MSN TCP webcam stream | Container | `msnwctcp` | demux + mux | missing | low | [#1468](https://github.com/Hawkynt/PNGCrushCS/issues/1468) |
| MTV (Chinese MP4 player) | Container | `.mtv` | demux + mux | missing | low | [#1470](https://github.com/Hawkynt/PNGCrushCS/issues/1470) |
| MVR CCTV | Container | `.mvr` | demux + mux | missing | low | [#1471](https://github.com/Hawkynt/PNGCrushCS/issues/1471) |
| NC camera feed | Container | `.v` | demux + mux | missing | low | [#1472](https://github.com/Hawkynt/PNGCrushCS/issues/1472) |
| NuppelVideo / MythTV NUV | Container | `.nuv` | demux + mux | missing | low | [#1473](https://github.com/Hawkynt/PNGCrushCS/issues/1473) |
| NXV (Adragon MP4 watch) | Container | `.nxv` | demux + mux | missing | low | [#1475](https://github.com/Hawkynt/PNGCrushCS/issues/1475) |
| Panasonic WJ-RT416 DVR (RT4) | Container | `.rt4` | demux + mux | missing | low | [#1478](https://github.com/Hawkynt/PNGCrushCS/issues/1478) |
| Playdate Video (PDV) | Container | `.pdv` | demux + mux | missing | low | [#1480](https://github.com/Hawkynt/PNGCrushCS/issues/1480) |
| PlayStation Portable PMP | Container | `.pmp` | demux + mux | missing | low | [#1481](https://github.com/Hawkynt/PNGCrushCS/issues/1481) |
| RealNetworks IVR (Internet Video Recording) | Container | `.ivr` | demux + mux | missing | low | [#1483](https://github.com/Hawkynt/PNGCrushCS/issues/1483) |
| SDR2 | Container | `.sdr2` | demux + mux | missing | low | [#1484](https://github.com/Hawkynt/PNGCrushCS/issues/1484) |
| Silicon Graphics Movie (MV) | Container | `.mv`, `.mov` | demux + mux | missing | low | [#1489](https://github.com/Hawkynt/PNGCrushCS/issues/1489) |
| TechnoTrend PVA | Container | `.pva` | demux + mux | missing | low | [#1493](https://github.com/Hawkynt/PNGCrushCS/issues/1493) |
| TiVo TY stream | Container | `.ty`, `.ty+` | demux + mux | missing | low | [#1494](https://github.com/Hawkynt/PNGCrushCS/issues/1494) |
| VGM / XVD (VideoGram) | Container | `.vgm`, `.vg2`, `.xvd` | demux + mux | missing | low | [#1496](https://github.com/Hawkynt/PNGCrushCS/issues/1496) |
| Video CCTV DAT (luo) | Container | `.dat` | demux + mux | missing | low | [#1499](https://github.com/Hawkynt/PNGCrushCS/issues/1499) |
| Vodei multimedia container | Container | `.avi` | demux + mux | missing | low | [#1502](https://github.com/Hawkynt/PNGCrushCS/issues/1502) |
| Windows Television (WTV) | Container | `.wtv` | demux + mux | missing | low | [#1505](https://github.com/Hawkynt/PNGCrushCS/issues/1505) |
| Matrox Uncompressed SD (M101/M102) encoder | Encoder | `M101`, `M102` | encode | decoder only | low | [#1455](https://github.com/Hawkynt/PNGCrushCS/issues/1455) |
| On2 VP5 encoder | Encoder | `VP50` | encode | decoder only | low | [#1477](https://github.com/Hawkynt/PNGCrushCS/issues/1477) |

## Screen-capture video

Screen recording and remote-desktop codecs. Tracked in [#526](https://github.com/Hawkynt/PNGCrushCS/issues/526).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Fraps (FPS1) | Codec | `.avi` | decode + encode | missing | medium | [#1509](https://github.com/Hawkynt/PNGCrushCS/issues/1509) |
| TechSmith Screen Capture (TSCC) encoder | Encoder | `TSCC`, `tscc` | encode | decoder only | medium | [#1510](https://github.com/Hawkynt/PNGCrushCS/issues/1510) |
| Amuse Graphics Movie (AGM0-AGM7) | Codec | `agm`, `AGM0`, `AGM1` | decode + encode | missing | low | [#1511](https://github.com/Hawkynt/PNGCrushCS/issues/1511) |
| Dxtory | Codec | `.avi` | decode + encode | blocked | low | [#1513](https://github.com/Hawkynt/PNGCrushCS/issues/1513) |
| FM Screen Capture Codec | Codec | `.avi` | decode + encode | blocked | low | [#1514](https://github.com/Hawkynt/PNGCrushCS/issues/1514) |
| Giga Link Video Codec (GLCC) | Codec | `GLCC` | decode + encode | missing | low | [#1515](https://github.com/Hawkynt/PNGCrushCS/issues/1515) |
| Go2Meeting / GoToMeeting (G2M2-G2M5) | Codec | `.asf`, `.avi` | decode + encode | blocked | low | [#1516](https://github.com/Hawkynt/PNGCrushCS/issues/1516) |
| LEAD Screen Capture | Codec | `.avi` | decode + encode | missing | low | [#1518](https://github.com/Hawkynt/PNGCrushCS/issues/1518) |
| Microsoft Office Camcorder Video (CGDI) | Codec | `CGDI` | decode + encode | missing | low | [#1521](https://github.com/Hawkynt/PNGCrushCS/issues/1521) |
| Mirillis FIC | Codec | `.avi` | decode + encode | missing | low | [#1523](https://github.com/Hawkynt/PNGCrushCS/issues/1523) |
| MS ATC Screen (MSA1) | Codec | `.wmv`, `.avi` | decode + encode | missing | low | [#1524](https://github.com/Hawkynt/PNGCrushCS/issues/1524) |
| MS Expression Encoder Screen (MTS2) | Codec | `.xesc` | decode + encode | missing | low | [#1525](https://github.com/Hawkynt/PNGCrushCS/issues/1525) |
| MSU Screen Capture Lossless Codec | Codec | `SCLS` | decode + encode | missing | low | [#1526](https://github.com/Hawkynt/PNGCrushCS/issues/1526) |
| RivaTuner Video (RTV1) | Codec | `.avi` | decode + encode | missing | low | [#1527](https://github.com/Hawkynt/PNGCrushCS/issues/1527) |
| ScreenPressor (SCPR) | Codec | `.avi` | decode + encode | blocked | low | [#1529](https://github.com/Hawkynt/PNGCrushCS/issues/1529) |
| TechSmith Screen Codec 2 (TSC2) | Codec | `.avi`, `.camrec` | decode + encode | blocked | low | [#1531](https://github.com/Hawkynt/PNGCrushCS/issues/1531) |
| Windows Media Video 9 Screen (MSS2) | Codec | `.wmv` | decode + encode | blocked | low | [#1533](https://github.com/Hawkynt/PNGCrushCS/issues/1533) |
| CamStudio (CSCD) encoder | Encoder | `CSCD` | encode | decoder only | low | [#1512](https://github.com/Hawkynt/PNGCrushCS/issues/1512) |
| innoHeim/Rsupport Screen Capture (RSCC) encoder | Encoder | `RSCC`, `ISCC` | encode | decoder only | low | [#1517](https://github.com/Hawkynt/PNGCrushCS/issues/1517) |
| Mandsoft Screen Capture (MSCC) encoder | Encoder | `MSCC` | encode | decoder only | low | [#1519](https://github.com/Hawkynt/PNGCrushCS/issues/1519) |
| MatchWare Screen Capture (MWSC) encoder | Encoder | `MWSC` | encode | decoder only | low | [#1520](https://github.com/Hawkynt/PNGCrushCS/issues/1520) |
| Microsoft Screen 1 (MSS1) encoder | Encoder | `MSS1` | encode | decoder only | low | [#1522](https://github.com/Hawkynt/PNGCrushCS/issues/1522) |
| Screenpresso (SPV1) encoder | Encoder | `SPV1` | encode | decoder only | low | [#1528](https://github.com/Hawkynt/PNGCrushCS/issues/1528) |
| TDSC (TDSC screen capture) encoder | Encoder | `TDSC` | encode | decoder only | low | [#1530](https://github.com/Hawkynt/PNGCrushCS/issues/1530) |
| WinCAM Motion Video (WCMV) encoder | Encoder | `WCMV` | encode | decoder only | low | [#1532](https://github.com/Hawkynt/PNGCrushCS/issues/1532) |

## Lossless & uncompressed video

Lossless intermediate and uncompressed video codecs. Tracked in [#527](https://github.com/Hawkynt/PNGCrushCS/issues/527).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| DNxUncompressed (SMPTE RDD 50) | Codec | `.mxf`, `.mov` | decode + encode | missing | medium | [#1534](https://github.com/Hawkynt/PNGCrushCS/issues/1534) |
| Lagarith lossless | Codec | `.avi` | decode + encode | blocked | medium | [#1535](https://github.com/Hawkynt/PNGCrushCS/issues/1535) |
| Alparysoft Lossless (ASLC) | Codec | `ASLC` | decode + encode | missing | low | [#1536](https://github.com/Hawkynt/PNGCrushCS/issues/1536) |
| AMV3 lossless | Codec | `AMV3` | decode + encode | missing | low | [#1537](https://github.com/Hawkynt/PNGCrushCS/issues/1537) |
| Bitpacked (RFC 4175 packed YUV 4:2:2) | Codec | `bitpacked`, `UYVY` | decode + encode | missing | low | [#1538](https://github.com/Hawkynt/PNGCrushCS/issues/1538) |
| FastCodec (FCKK) | Codec | `FCKK` | decode + encode | missing | low | [#1539](https://github.com/Hawkynt/PNGCrushCS/issues/1539) |
| Forward Uncompressed | Codec | `.avi` | decode + encode | missing | low | [#1540](https://github.com/Hawkynt/PNGCrushCS/issues/1540) |
| HuffYUV MT | Codec | `.avi` | decode + encode | blocked | low | [#1541](https://github.com/Hawkynt/PNGCrushCS/issues/1541) |
| IgCodec (IGC1-IGC4) | Codec | `IGC1`, `IGC2`, `IGC3` | decode + encode | missing | low | [#1542](https://github.com/Hawkynt/PNGCrushCS/issues/1542) |
| Luna Lossless Video Codec (LR1A, LY1A) | Codec | `LR1A`, `LY1A` | decode + encode | missing | low | [#1543](https://github.com/Hawkynt/PNGCrushCS/issues/1543) |
| Microcosm (Mczm) | Codec | `Mczm` | decode + encode | missing | low | [#1544](https://github.com/Hawkynt/PNGCrushCS/issues/1544) |
| Pinnacle TARGA CineWave YUV16 (Y216) | Codec | `.mov` | decode + encode | missing | low | [#1546](https://github.com/Hawkynt/PNGCrushCS/issues/1546) |
| Toponoky (LCKK) | Codec | `LCKK` | decode + encode | missing | low | [#1547](https://github.com/Hawkynt/PNGCrushCS/issues/1547) |
| YUY2 Lossless Codec (YLC) | Codec | `.avi` | decode + encode | blocked | low | [#1549](https://github.com/Hawkynt/PNGCrushCS/issues/1549) |
| MidiVid Archive (MVHA) encoder | Encoder | `MVHA` | encode | decoder only | low | [#1545](https://github.com/Hawkynt/PNGCrushCS/issues/1545) |
| VBLE Lossless encoder | Encoder | `VBLE` | encode | decoder only | low | [#1548](https://github.com/Hawkynt/PNGCrushCS/issues/1548) |

## Cinema camera RAW video

Cinema/phone camera raw video codecs and containers. Tracked in [#528](https://github.com/Hawkynt/PNGCrushCS/issues/528).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| Apple ProRes RAW | Codec | `.mov` | decode + encode | missing | medium | [#1550](https://github.com/Hawkynt/PNGCrushCS/issues/1550) |
| Blackmagic RAW codec | Codec | `.braw` | decode + encode | missing | medium | [#1552](https://github.com/Hawkynt/PNGCrushCS/issues/1552) |
| REDCODE RAW | Codec | `.r3d` | decode + encode | missing | medium | [#1554](https://github.com/Hawkynt/PNGCrushCS/issues/1554) |
| Blackmagic RAW (.braw) | Container | `.braw` | demux + mux | missing | medium | [#1551](https://github.com/Hawkynt/PNGCrushCS/issues/1551) |
| RED R3D (REDCODE RAW container) | Container | `.r3d`, `.rdc` | demux + mux | missing | medium | [#1553](https://github.com/Hawkynt/PNGCrushCS/issues/1553) |
| ARRIRAW / ARRIRAW HDE | Codec | `.ari`, `.arx` | decode + encode | missing | low | [#1556](https://github.com/Hawkynt/PNGCrushCS/issues/1556) |
| Canon Cinema RAW Light | Codec | `.crm` | decode + encode | missing | low | [#1557](https://github.com/Hawkynt/PNGCrushCS/issues/1557) |
| Sony X-OCN (eXtended tonal range Original Camera Negative) | Codec | `.mxf` | decode + encode | missing | low | [#1561](https://github.com/Hawkynt/PNGCrushCS/issues/1561) |
| ARRIRAW (.ari frame files / .arx / ARRIRAW in MXF) | Container | `.ari`, `.arx` | demux + mux | missing | low | [#1555](https://github.com/Hawkynt/PNGCrushCS/issues/1555) |
| Canon Cinema RAW Light (.crm) | Container | `.crm`, `.rmf` | demux + mux | missing | low | [#1558](https://github.com/Hawkynt/PNGCrushCS/issues/1558) |
| Magic Lantern Video (MLV) | Container | `.mlv`, `.m00`, `.m01` | demux + mux | missing | low | [#1559](https://github.com/Hawkynt/PNGCrushCS/issues/1559) |
| MotionCam RAW (.mcraw) | Container | `.mcraw` | demux + mux | missing | low | [#1560](https://github.com/Hawkynt/PNGCrushCS/issues/1560) |
| Vision Research Phantom Cine | Container | `.cine` | demux + mux | missing | low | [#1562](https://github.com/Hawkynt/PNGCrushCS/issues/1562) |

## Elementary streams & streaming manifests

Raw codec byte streams and adaptive-streaming manifests. Tracked in [#529](https://github.com/Hawkynt/PNGCrushCS/issues/529).

| Format | Kind | Extensions / ids | Needs | Status | Priority | Issue |
| --- | --- | --- | --- | --- | --- | --- |
| HTTP Live Streaming playlist (HLS, M3U8) | Container | `.m3u8`, `.m3u` | demux + mux | missing | high | [#1563](https://github.com/Hawkynt/PNGCrushCS/issues/1563) |
| MPEG-DASH Media Presentation Description (MPD) | Container | `.mpd` | demux + mux | missing | high | [#1564](https://github.com/Hawkynt/PNGCrushCS/issues/1564) |
| AV1 Annex B length-delimited bitstream | Container | `.obu`, `.av1` | demux + mux | missing | medium | [#1565](https://github.com/Hawkynt/PNGCrushCS/issues/1565) |
| Dirac / VC-2 elementary stream | Container | `.drc`, `.vc2` | demux + mux | missing | medium | [#1566](https://github.com/Hawkynt/PNGCrushCS/issues/1566) |
| H.261 elementary stream | Container | `.h261` | demux + mux | missing | medium | [#1567](https://github.com/Hawkynt/PNGCrushCS/issues/1567) |
| H.266/VVC Annex B byte stream | Container | `.vvc`, `.266`, `.h266` | demux + mux | missing | medium | [#1568](https://github.com/Hawkynt/PNGCrushCS/issues/1568) |
| MPEG-4 Part 2 elementary stream | Container | `.m4v`, `.cmp`, `.xvid`, `.mpg4` | demux + mux | missing | medium | [#1569](https://github.com/Hawkynt/PNGCrushCS/issues/1569) |
| Raw DV stream (DIF) | Container | `.dv`, `.dif` | demux + mux | missing | medium | [#1570](https://github.com/Hawkynt/PNGCrushCS/issues/1570) |
| VC-1 elementary stream (SMPTE 421M Annex G/E) | Container | `.vc1` | demux + mux | missing | medium | [#1571](https://github.com/Hawkynt/PNGCrushCS/issues/1571) |
| VC-1 RCV test bitstream (Annex L) | Container | `.rcv` | demux + mux | missing | medium | [#1572](https://github.com/Hawkynt/PNGCrushCS/issues/1572) |
| Adobe HTTP Dynamic Streaming manifest (F4M) / F4F fragments | Container | `.f4m`, `.f4f` | demux + mux | missing | low | [#1573](https://github.com/Hawkynt/PNGCrushCS/issues/1573) |
| AVS2-P2 (IEEE 1857.4) elementary stream | Container | `.avs2`, `.avs` | demux + mux | missing | low | [#1574](https://github.com/Hawkynt/PNGCrushCS/issues/1574) |
| AVS3-P2 (IEEE 1857.10) elementary stream | Container | `.avs3` | demux + mux | missing | low | [#1575](https://github.com/Hawkynt/PNGCrushCS/issues/1575) |
| Chinese AVS1-P2 (Jizhun) elementary stream | Container | `.cavs` | demux + mux | missing | low | [#1576](https://github.com/Hawkynt/PNGCrushCS/issues/1576) |
| Microsoft Smooth Streaming manifest (ISM/ISMC) | Container | `.ismc`, `.ism`, `.isml` | demux + mux | missing | low | [#1577](https://github.com/Hawkynt/PNGCrushCS/issues/1577) |
| MPEG-5 EVC Annex B byte stream | Container | `.evc` | demux + mux | missing | low | [#1578](https://github.com/Hawkynt/PNGCrushCS/issues/1578) |
| Raw DNxHD / DNxHR stream (VC-3) | Container | `.dnxhd`, `.dnxhr`, `.vc3` | demux + mux | missing | low | [#1579](https://github.com/Hawkynt/PNGCrushCS/issues/1579) |
| Samsung APV raw bitstream | Container | `.apv` | demux + mux | missing | low | [#1580](https://github.com/Hawkynt/PNGCrushCS/issues/1580) |
