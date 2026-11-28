# MrSID (`.sid`) — derived container description

LizardTech, now Extensis, never published MrSID. This document comes from reading the files. It covers the container
that `MrSidReader` reads: the fixed headers of generations 2, 3 and 4, the metadata database and the georeferencing.
It also records what can be seen of the generation 2 codestream without decoding it. The coding itself, meaning the
wavelet filters, quantisation and coefficient coding, was **not** recovered, and nothing here should be read as a
description of it.

## Provenance and why the pixels are refused

* **Rung 1.** There is no open implementation in any licence. GDAL's MrSID driver (MIT) is a wrapper around the vendor
  SDK. It contains no layout knowledge beyond the `msid` signature, a call to the SDK's
  `getMrSIDGeneration(header, gen, raster)`, and, in older versions, the rule that a file version below 2 is
  reported as MG2.
* **Rung 3.** The format has no specification. The Library of Congress descriptions (FDD 000031, FDD 000184) say so
  ("No public specification"; "Equivalent tools could only be built with full access to the proprietary
  specifications"). US 5,710,835 (Bradley et al., LANL, expired) describes the method MrSID grew from: tiled
  separable DWT with seam corrections between tiles, per-subband scalar quantisation, zero-run-length and Huffman
  coding of the quantiser indices in "Huffman blocks", each with its own table and an offset table for browsing.
  It gives no bitstream and no filter coefficients. What the generation 2 files show fits that description (see
  below), but fitting is not the same as being specified.
* **The vendor decoder cannot be used.** The free MrSID Decode SDK is the only decoder. Every product that reads
  MrSID (GDAL/QGIS builds, XnView, IrfanView, GeoViewer) links it, and §1.3(d) of its licence requires the same
  restrictions to pass down to every sublicensee. The licence, as published at
  `https://www.lizardtech.com/mrsidsdkeula` (where Extensis' download page redirects) and shipped with OSGeo4W's
  `gdal-mrsid` package:

  > 1.1 DSDK. … Licensee shall not modify, reverse engineer, disassemble or decompile or otherwise seek to discover
  > the source code or trade secrets of DSDK. Licensee shall not use DSDK to develop products or allow others to
  > develop products that encode files into the MrSID format.
  >
  > 1.2 Development License Grant. Extensis hereby grants to Licensee a nonexclusive license to install DSDK on a
  > reasonable number of CPUs in Licensee's own facilities only for its own internal use and development of Licensed
  > Products that use DSDK to view *.sid files.
  >
  > 1.4 Rights in DSDK. … DSDK, including features and related information, is unpublished software, trade secret,
  > confidential or proprietary information of Extensis or its licensors.

  Disassembly is excluded by 1.1. Black-box use is excluded as well. 1.2 grants installation only for developing
  products that view `.sid` files *through the DSDK*, and comparing a clean decoder against the DSDK in order to
  recover its algorithm is "seek[ing] to discover the … trade secrets of DSDK". The SDK was therefore not downloaded,
  installed or run. GeoViewer, Extensis' free viewer, has the same clause in its licence (§1.4: "You may not
  modify, reverse engineer, disassemble or decompile, or otherwise seek to discover the source code of the
  Software"). It is further limited to "internal business use only" (§1.2) and forbids publishing "any results of
  any benchmark tests run on the Software" (§1.4).
* **Without an oracle there is no decoder.** A reconstruction of the coding from the files alone could not be
  checked against anything. The repository does not ship a decoder that can't be checked, so `ToRawImage` refuses
  by name.
* **What served as evidence.** Real files and their own self-descriptions. GDAL's test suite (MIT) asserts values
  for `mercator.sid` and `mercator_new.sid` that came out of the DSDK: the size, four overviews for the MG2 file,
  the geotransform and the WKT. A GDAL `.aux.xml` that ships beside LizardTech's own MG4 sample records its
  geotransform. These are captured vendor output, not runs of the vendor code, and the tests hold the reader to
  them.
* **Rung of the container description.** Rung 4/5: my own analysis of real files, cross-checked across files and
  against the captured values above. Where a field is settled by one file only, it is marked below.

## Files examined

| File | Source | Generation | Size | Bands | Levels | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `mercator.sid` | GDAL autotest (fixture) | MG2, metadata | 515×515 | 1 | 4 | GeoTIFF keys, encoder settings, written 2003 |
| `mercator_new.sid` | GDAL autotest (fixture) | MG3 | 515×515 | 1 | 5 | WKT, georeference box |
| `ABACUS.SID`, `cd2cos07i01.sid`, `EXCHANGE.sid`, `oxford.sid`, `spurn.sid`, `Test.sid`, `U.SID`, `WEST.SID` | sembiance.com `mrSIDImage` | MG2 | 431×338 … 8400×6000 | 1 or 3 | 3–5 | no metadata; `oxford`, `U`, `WEST` have several lowest-level tiles |
| `meg_cr20.sid` | sembiance.com | MG3 | 640×480 | 3 | 4 | metadata before the codestream |
| `UAE_crop1_100to1.sid` | LizardTech's sample page | MG4 (`IMAGE::FORMAT` = `MrSID/MG4`) | 8065×7297 | 4 | 7 | GeoExpress 10.0.1; metadata after the codestream |
| `Tetons_200k.xyz.sid` | sembiance.com | MG4 LiDAR | — | — | — | point cloud; refused |

Only the two GDAL files are committed. The others are read by `MrSidTests.DecodesTheCorpus` when `MRSID_CORPUS`
points at them.

## Common

Every multi-byte field is big-endian. Bytes 0–3 are `msid`, and byte 4 selects the layout:

| Byte 4 | Layout |
| --- | --- |
| 1 | MG2. GDAL's pre-SDK-8 code maps every file version below 2 to MG2. |
| 3 | MG3 container. A minor version (byte 5) of 4 marks MG4. |
| 4 | MG4 LiDAR point cloud. GDAL's raster driver declines these (`raster == false`). |

## Generation 2

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | `msid` |
| 4 | 1 | 1 |
| 5 | 2 | 1 when an `FF D0` segment follows the offset table, else 0 (two files) |
| 7 | 4 | band count (1, 3) |
| 11 | 4 | wavelet levels; GDAL reports this many overviews for `mercator.sid` |
| 15 | 4 | width |
| 19 | 4 | height |
| 23 | 4 | header length = offset of the tile directory (`FF AC`) |
| 27 | 4 × 3·levels | subband offset table |
| … | 6 each | optional segments up to the header length: `FF xx` + 32-bit value |

* **Offset table.** Every entry points at an `FF AC` marker followed by a 16-bit tag and four zero bytes. The tag
  depends only on the entry's index, in all nine files: `5209 222E 207E 2877 166D 0401 503E 2266 0B39 517C 5465 4805
  7008 257E 0902`. The meaning of the tags is unknown.
* **`FF D0` segment.** The 32-bit value is the offset of an `FF D1` metadata segment, `FF D1 00 01 00 00`, followed
  by records (below) up to an `FF D2` marker. The file ends with `FF A1`.
* **Tile directory** at the header length: `FF AC`, 16 bits (0x001E in every file), a 32-bit tile count, then per tile
  `bands` 32-bit offsets followed by the tile's first column, first row, last column and last row, inclusive and in
  samples of the lowest level. One tile covers at most 253 of those samples per side. In `oxford.sid`
  (4942×5441, 4 levels) the four tiles split at 253, which is about 4048 full-resolution pixels. Every band offset
  points at an `FF AA` segment.
* **`FF AA` segments** (2125 across the nine files) all read as a canonical Huffman table in the JPEG DHT layout:
  16 code-length counts, then that many symbols. Every one has distinct symbols and a Kraft sum ≤ 1. The byte after
  the symbols is 0x40, 0x41 or 0x42. What the symbols stand for is
  unknown.
* **Other markers.** `FF A3` occurs often inside the coded data, and `FF 00` occurs as in JPEG byte stuffing.
  Neither has been interpreted.
* **Bits per sample.** There is no header field. `IMAGE::BITS_PER_SAMPLE` gives it when present; otherwise the reader
  reports 8.

## Generations 3 and 4

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | `msid` |
| 4 | 1 | 3 |
| 5 | 1 | minor version: 0 (MG3 files), 4 (the MG4 file) |
| 6 | 2 | differs per encoder (0x1A6F, 0x1A71, 0x0061); unknown |
| 8 | 2 | 0x0103 in all three |
| 0x1A | 4 | width |
| 0x1E | 4 | height |
| 0x22 | 1 | wavelet levels (4, 5, 7) |
| 0x24 | 1 | band count (3, 1, 4) |
| 0x25 | 1 | 0x20 in all three; unknown |
| 0x27 | 1 | 0x40 in all three; unknown |
| 0x28 | 1 | bits per sample (8 in all three) |
| 0x30 | 4 | differs (`FF 0A 08 0A`, `FF 01 01 01`, `FF 4A 08 0A`); unknown |
| 0x38 | 2 | 0x0004 in all three |
| 0x3A | | boxes, to the end of the file |

The same 515×515 picture has 4 levels in its MG2 file and 5 in its MG3 file, and the MG2 file's own
`IMAGE::COMPRESSION_NLEV` says 4. Byte 0x22 is read as the level count on that basis. Only the MG2 count is confirmed
by GDAL.

Boxes, with a 64-bit payload length as the last field of the box header:

| Header | Payload |
| --- | --- |
| `02 43 01` + length | metadata: 32-bit length − 4, `00 01 00 00`, records up to `FF FF FF FF` |
| `02 43 03` + length | georeference: 32-bit 49, one byte (1), then six 64-bit floats: centre X and Y of the top-left pixel, centre X and Y of the bottom-right pixel, pixel width, pixel height (negative) |
| `04 03` + length | codestream (MG3 `meg_cr20.sid`) |
| `44 03 00 01` + length | codestream (`mercator_new.sid`, the MG4 file) |

The MG3 files store metadata, then the georeference box (if any), then the codestream. The MG4 file stores the
codestream first. Every codestream payload begins `01 02 00 00 00 00 00 00`; nothing after that has been
interpreted. The grammar of the box headers is inferred from three files and is a hypothesis. The reader stops at
any byte it does not recognise as a box start, and refuses a file with no codestream box or with a box that runs past
the end. The MG3 composite (mosaic) layout has not been seen.

The pixel geometry matches GDAL in both files that have it. Both georeference boxes give GDAL's geotransform once
half a pixel is taken off the stored centre: GDAL's tests for `mercator_new.sid`, and the `.aux.xml` GDAL wrote
beside `UAE_crop1_100to1.sid`.

## Metadata records

The same layout is used in all generations:

| Field | Size |
| --- | --- |
| length = key length + 16 | 4 |
| key | length − 16 |
| NUL | 1 |
| type | 4 |
| element count | 4 |
| dimension count | 4 |
| each dimension | 4 |
| elements | count × size; strings NUL-terminated |

The record length covers the key only, so a record of an unknown type cannot be skipped. The reader stops there and
sets `HasUnreadMetadata`.

| Type | Values | Settled by |
| --- | --- | --- |
| 1 | `uint8` | `IMAGE::NO_DATA_VALUE` (three zeros) |
| 2 | `uint16` | `IMAGE::BITS_PER_SAMPLE` = 8, GeoTIFF key codes (4267, 9001, 32640) |
| 3 | `uint32` | `IMAGE::WIDTH`, `IMAGE::HEIGHT` agree with the header |
| 5 | strings | `IMAGE::WKT`, `IMAGE::INPUT_NAME` |
| 6 | `float32` | `IMAGE::TARGET_COMPRESSION_RATIO` ≈ 15 (0x416FFFFF), `COMPRESSION_GAMMA` = 2.0 |
| 7 | `float64` | `IMAGE::INPUT_FILE_SIZE` = 921600 = 640·480·3 for an 8-bit RGB input |
| 10 | 4-byte integer, read as signed | `IMAGE::COMPRESSION_VERSION` = 1, 4, 1 (one file) |

Types 4, 8 and 9 have not been seen. The list starts with `IMAGE::SOM` and ends with `IMAGE::EOM` (both `uint16` 1).
GeoTIFF keys appear as `GEOTIFF_NUM::<code>::<name>` (value) and `GEOTIFF_CHAR::<name>` (value name).
`IMAGE::COLOR_SCHEME` is 0 in the RGB MG3 file, 3 in both grey files and 13 in the four-band MG4 file, whose
`IMAGE::MODIFICATIONS` reads `COMPRESSED CROPPED OPAQUE-ALPHA`. The code-to-colour-space mapping is not known, so
the reader reports 1 band as grey, 3 as RGB and any other count as "N bands".

When there is no georeference box, the georeferencing comes from `IMAGE::XY_ORIGIN` (the centre of the top-left
pixel) together with `IMAGE::X_RESOLUTION` and `IMAGE::Y_RESOLUTION`. Both resolutions are stored as positive
distances, and rows run south, which reproduces GDAL's geotransform for `mercator.sid`.

## Unknown

* The whole coefficient coding of every generation: the wavelet filters, the colour transform (FDD 000184 mentions a
  default RGB-to-YIQ transform), the quantisation, the use of the `FF AA` tables, `FF A3`, the 0x40–0x42 byte after
  each table, and the `FF AC` tags. The same applies to the MG3/MG4 codestream payload after its first eight bytes.
* The unnamed fixed-header fields listed above, the meaning of `IMAGE::COLOR_SCHEME` codes, and metadata types 4, 8
  and 9.
* MG3/MG4 composite (mosaic) files, 16-bit and floating-point MG4 rasters, and lossless files. No sample of any of
  them was available.
* Writing. Even with the coding known, the only encoder is Extensis' commercial GeoExpress, and the DSDK licence
  forbids using the DSDK for developing encoders. There is nothing to verify a writer against.
