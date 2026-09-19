# Image formats investigated and not implemented

The sibling of [`codec-investigations.md`](https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-investigations.md)
for the image package, and it exists for the same reason: a format that was chased and not
implemented is a result, not a gap, and writing down where it stopped is what stops the next person
repeating the chase. The package's own support table lives in `README.md` and is generated from
`FormatRegistry`, so nothing here has a row there — a format in this file is one the registry
deliberately does not claim.

## LPG (Xerox "Paris")

**Status: no implementation, and nothing to implement from.** Not a hard format, not a licence
problem, not a missing table. There is no file, no signature entry in any public database, and no
reference implementation that admits knowing the format. The search below was run on 2026-09-14 and
2026-09-15 and is recorded because it was thorough enough to be worth not repeating.

What was being chased: `LPG`, a raster format attributed to the Xerox "Paris" publishing and
print-stream product line, with AccuSoft's ImageGear named as the library most likely to still carry
a reader for it. The plan was the usual one for this repository — obtain a sample, obtain an outside
oracle, implement clean-room against both. Neither half was ever obtained.

### No sample file is in reach

| Source | Queried for | Result |
| --- | --- | --- |
| `download.support.xerox.com` — `/pub/docs/Paris/` and `/pub/drivers/Paris/` trees, plus three named manual PDFs | directory listings, `Paris_Designer_UM.pdf`, `Paris_Converter_TM.pdf`, `Paris_Spooler32_TM.pdf` | every path **404**, directory roots **403**. No Xerox document was ever retrieved. |
| DiscMaster (`discmaster.textfiles.com`) | `MS-LOGO.LPG`, `PARISPM.DLL`, `STOCKSET.XPI`, `GREYSCAL.FRM`, `PHONEP.LGO`, `RainDay.pcx`, `Robsig.pcx`, `Robsig1.pcx` | **0 results** for every one, in every run that asked. |
| DiscMaster | `extension=lpg` | non-empty, and none of it is this format — see below. |
| DiscMaster | `XLPrint`, `Lapres`, `PMCONFIG.EXE`, `mercury1.pcx`, `GREYSCAL.PAL`, `PACK1.PAK`, `Paris Designer` | hits, all substring collisions. The 20 `XLPrint` hits are Excel's `XlPrintErrors`/`XlPrintLocation` VBA enum classes, a ZX Spectrum `ZXLPRINT.SNA`, and Mentalix `pxlprint.html`. `Paris Designer` matched German Eclipse `appdesigner` help files containing no "Paris" at all. |
| Internet Archive advanced search | `"XLPrint"`, `"Paris Document System"`, `"Laser Image Technologies" AND Paris`, `"ftp.XLPrintau.dynip.com"` | `numFound: 0` for each. |
| Wayback availability API | `xlprint.com`, `www.xlprint.com`, `ftp.XLPrintau.dynip.com`, and paths beneath them | `archived_snapshots: {}` for every path but one — see the `xlprint.com` capture below. |
| Arquivo.pt CDX | same hosts | HTTP 200, zero-byte bodies. |
| urlscan.io | `filename:MS-LOGO.LPG` and siblings | 0 hits. |

Two sources returned service errors rather than answers and are therefore **inconclusive, not
negative**: the Wayback CDX endpoint and all twelve Common Crawl indexes answered 503/504 or timed
out for every query. If this is ever picked up again, those two are the only sources in this record
that were never actually answered, and so the only ones worth asking again.

The one thing the search did turn up is `xlprint.com` itself, captured once on 2001-02-01 at
`web.archive.org/web/20010201090400/http://www.xlprint.com:80/`. The probes only asked whether that
capture existed and never read it, which made it look like an open lead; it is not. The capture is a
frameset, and its `content.html` frame is an empty `Untitled Document`. Nothing of the site's
substance was archived, and no deeper path under that host has a snapshot at all.

The `.lpg` extension is reused by unrelated software, which is why an extension search looks
promising and is not. Of the hits that were examined, they divide into Lout document-formatter
include files (`dl.lpg`, `graph.lpg`, `teq.lpg` in `lout-3.xx` trees), LALR Parser Generator grammar
tables shipped with Eclipse's `org.eclipse.datatools.sqltools`, and two that fitted neither, both of
which were downloaded and opened:

- `Lake5.lpg`, on a Japanese MacUser CD-ROM in an image directory, which looked the most promising
  thing the whole search produced. It is a MacBinary wrapper — Mac type `JPEG`, creator `8BIM` — and
  its data fork begins `FF D8 FF E0 … JFIF … Photoshop 3.0`. A Photoshop JPEG under a misleading
  extension.
- `dosver.lpg`, four bytes, all `0x20`.

### No signature database has ever heard of it

TrID's current definition package, TrID's published extension list, the local libmagic database, the
freedesktop MIME database, Apache Tika's `tika-mimetypes.xml` and `file(1)`'s own `Magdir/images`
were each searched for `LPG`, `XLPrint`, `Paris`-plus-graphic and `Lowrey`. Every one came back
empty. The only things that matched at all were a TrID record for `PA-RISC relocatable library` —
`PARIS` is a substring of `PA-RISC` — and a run of unprintable bytes inside TrID's binary database
that happened to spell `v3LpG`. Neither is a signature entry.

### ImageGear was obtained, made to run, and does not know the format

This is the most concrete result of the whole investigation, and the one worth keeping. Two vintages
of AccuSoft's ImageGear were recovered and executed as a black-box oracle under Wine — no ImageGear
code was read or carried across, only its answers:

| Build | sha256 | Provenance |
| --- | --- | --- |
| ImageGear 7 `GEAR32SD.DLL` (7.0.15.15) | `a42787bc5c747f306169b28d5d0e7b71c16ad48a4bc11b2b434117d349e130c2` | dll-files.com |
| ImageGear `GEAR32SD.DLL`, dated 1998-08-28 | `fb7685dd92fc8ac3e4aa03562f0960fac79599c171fa0bbbf9e5bd4dc9406602` | `Canvas7/DefaultProgram.Cab` inside the Canvas 7.0.2 ISO on archive.org |

Getting them to answer took two corrections worth recording. There is no initialisation entry point:
`IG_initialize` and `IG_close` do not exist as exports in either build — `objdump -p` lists
`IG_info_get`, `IG_ext_info_get`, `IG_load_file`, `IG_image_dimensions_get` and friends and nothing
resembling an init — so `IG_info_get` is called directly. And the exports are `__stdcall`, not
`__cdecl`; declared as `__cdecl` the probe faulted inside Wine
(`Unhandled page fault on read access to 00000013`) before producing any output.

Corrected, the oracle works, which is what makes its verdict meaningful:

```
INFO known.bmp    rc=0 fmt=2  comp=0
LOAD known.bmp    rc=0 h=0024ef50 dims_rc=0 1x1 bpp=24
INFO known.png    rc=0 fmt=33 comp=14
```

It identifies and fully loads a known-good BMP, and identifies a known-good PNG. Both builds agree,
to the format code.

Neither build carries an LPG reader. The filter modules are recoverable by name from the DLLs' own
embedded debug paths (`filters\PCXREAD.C` and the like), and the recovered set is 42 read filters
and their writers — `ABCread`, `Afxread`, `AVIREAD`, `Bmpread`, `btrread`, `CALREAD`, `CLPREAD`,
`CUTREAD`, `DCXREAD`, `epsread`, `G34READ`, `gemread`, `GIFREAD`, `ICAREAD`, `ICOREAD`, `IFFREAD`,
`IMGREAD`, `IMRread`, `IMTREAD`, `JBGread`, `JPGREAD`, `KFXREAD`, `LVREAD`, `MACREAD`, `MSPREAD`,
`NCRREAD`, `Pbmread`, `PCDREAD`, `PCTREAD`, `PCXREAD`, `PNGREAD`, `PSDREAD`, `RASREAD`, `rawread`,
`SGIREAD`, `tgaread`, `TIFREAD`, `wmfread`, `WPGREAD`, `xbmread`, `xpmread`, `XWDREAD`. There is no
`LPGREAD` and no Paris filter of any name. A direct string search of both binaries for `LPG`, `IGF`,
`ImageGear Format`, `.IGF` and `.LPG` returned nothing whatsoever.

### The `50 50 03 00` header is a guess and must not be adopted

The probe carried a candidate header beginning `50 50 03 00` — ASCII `PP` then `03 00` — presented
to the oracle under three extensions. It is a hypothesis with no stated derivation: it was written
as a Python hex literal in the first probe that ran a DLL at all, and it survived unchanged through
every later commit without once being replaced by anything downloaded. No log in either branch
records it being read out of a file, because no LPG file was ever obtained to read it out of.

The oracle's verdict on it is unambiguous, and identical in both builds:

```
INFO white.bin    rc=1 fmt=0 comp=0
INFO white.igf    rc=1 fmt=0 comp=0
INFO white.lpg    rc=1 fmt=0 comp=0
INFO black.bin    rc=1 fmt=0 comp=0
```

The candidate is rejected exactly as the deliberate-garbage control is, with no distinguishing
format code, whatever extension it is given. So `50 50 03 00` is not this repository's evidence for
anything, and nothing should be built on it. **This format has no known signature.**

One further caution for anyone resuming. The resource names the search was built around —
`MS-LOGO.LPG`, `GREYSCAL.FRM`, `PHONEP.LGO`, `PARISPM.DLL`, `STOCKSET.XPI` — are asserted in the
probe scripts as coming from vendor manuals and a version-3 launch document, but no such document
was ever retrieved: every Xerox manual URL 404'd. The search targets are therefore themselves
unverified, and a resumed hunt should re-establish them before spending anything on them.

### What would change the answer

A file. One real `.lpg` produced by Paris, with any provenance at all, turns this from an absence
into an ordinary reverse-engineering problem, and the ImageGear oracle above is already built and
already known to run. Nothing short of that helps: there is no specification to read, no signature
to register, and no oracle that claims the format.
