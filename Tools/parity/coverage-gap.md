# What we do not read that XnView says it does

Generated from XnView's own `Formats.txt` against `Decode --extensions`. RECOIL's catalogue is
covered but for `.gr10p`, which RECOIL comments out of its own list for having a five-character
extension, so this is the whole of the known coverage gap.

A name here is a file we cannot open. That is counted and closed rather than explained — unlike
the rendering differences in the report beside this, which are cases of the tool giving
something up and are correct as they stand.

**197 distinct extensions across 176 of its format names** when this was written. A few extensions
are claimed by more than one of its names, so the rows below add up to more than that.

**Eighty are closed now and 96 remain.** Eight of the fifteen turned out to be one thing — a
Windows DIB preview dropped inside a drawing or project file — and are read by a single reader
rather than eight. IBM KIPS, the X11 puzzle, Synu and the Zoner brush were four more. The last three
are wrappers around a picture format already here: ECC carries a PNG, LView Pro and IPSM each carry
a JPEG, and all three state a size the payload agrees with, which is what identifies the file rather
than a fixed offset guessed from one sample. Every one of the three was checked against ImageMagick
on the extracted payload and matches it on every pixel.

Of the 161 left, ten still have a sample here and the rest have none. That is what makes them hard
rather than tedious: a format with no sample, no specification and no tool on this machine that
reads it cannot be implemented without guessing a layout, and guessing has already been shown here
to produce readers that score well on a sample of pixels and are wrong over the whole picture. The
ten with samples are `afx`, `bmg`, `hru`, `pegs`, `pxa`, `tile`, `upe4`, `upst` and `vit`. What has
been measured about them, so it does not have to be measured again:

  - `pe4` and `pst` are tiled — sixty separate JPEGs in one file — so they need the tiles assembled
    rather than the first one drawn, which is the mistake a signature search alone would make.
  - `afx` opens with PNG's eight-byte signature carrying `AFX` in place of `PNG`, and is not chunked
    the way PNG is. It holds four JPEGs at 140x88, 128x80 and 125x128, which are previews at
    different sizes rather than one picture. Which of them the tool draws is exactly what cannot be
    settled without the tool, so it is left rather than guessed: drawing the largest would be picking
    one on no evidence.
  - `tile` names itself `Eclipse` at 16 and states two equal numbers at 4 and 8; `vit` names itself
    `VITec` at 32; `pxa` names itself `Pixia` at 0. Each has a header worth reading and none of them
    has been read.

The pattern that closed `ecc`, `lvp` and `pan` is the one to try first on the rest: find whether the
file carries a picture format already here, and if it does, require the header's stated size to agree
with the payload's own before drawing it.

### Names that belonged to a format already here

XnView's catalogue pairs a format name with the extensions that name reads, so where it names a
format this library already reads and gives an extension nothing here claims, claiming it closes the
row honestly — provided the reader would still refuse a file of some other format arriving under
that name. Seven were claimed on that test and three were declined on it.

  - `iff` wants `.blk`, which is an Amiga IFF bitmap under a name of its own. Claimed: the reader
    requires the group identifier and the form type before it reads anything.
  - `sct` wants `.ch`, `pspf` wants `.pfr`, `pmsk` wants `.msk` and `pspt` wants `.tex`. All four
    are claimed: Scitex CT is identified by two characters at offset 80 and Paint Shop Pro by the
    eight-byte string it opens with, so a font resource or a game texture under one of those names
    is refused rather than drawn.
  - `avs` wants `.mbfavs` and `.mbfs`. Claimed: an AVS raster has no signature at all, but its two
    lengths have to account for the file to the byte, which a foreign file does not do.
  - `hpgl` wants `.prn` and `.prt`, the names a driver gives a job printed to a file. Claimed
    because the parse decides, not the name: it requires an instruction that moves the pen and says
    where to, and all five PostScript samples here are refused by it.
  - `cloe` wants `.cloe`, which is the long name of the format read here as `.clo`. Claimed, after
    taking out of the reader the part that invented 320 by 200 whenever the header stated no size —
    which meant any file long enough was drawn as a picture of a size it never claimed.

Declined, and why:

  - `bfli` wants `.flp`. The BFLI reader validates nothing but the file's length, and it can be
    shown to: handed the IOCA sample in this corpus it reports a 320 by 200 picture. Claiming
    `.flp` would draw whatever was under that name.
  - `ioca` wants `.mod`. Worse — the IOCA reader falls back to reading the first four bytes of
    anything at all as a width and a height. `.mod` is an Amiga music module as often as anything
    else, and every one of them would be drawn.
  - `eps` wanted `.ps`, and was declined here for a while: the EPS reader reads the DOS binary
    wrapper and the TIFF preview inside it, and plain PostScript is a language rather than a layout.
    That is no longer the position — there is an interpreter now, and the entry below says what it
    does.

Three more were looked at and left: `aim` wants `.ima`, but the reader's "AIM\0" signature is not
sourced from anything and there is no sample, so the claim could not be shown to read a real file;
`icd` wants `.idc` and `pixi` wants `.pxb`, and neither is the format the similarly-named reader
here actually reads.

### The three that needed an interpreter rather than a reader

`ps`, `eps` and `ai` are one job: PostScript. A file of any of those names is a program, so what
closed them is an interpreter — the scanner, the operand and dictionary stacks, the control
constructs, and the graphics operators — running onto the vector rasteriser the drawing formats here
already share. The page is the `%%BoundingBox` the file states, at ninety-six pixels to the inch,
and the first `showpage` ends it.

Measured against Ghostscript, at the same page box, on the five PostScript samples in this corpus:
`tiger.ps` 727x756 and `GOLFER.PS` 760x927 both come out the size Ghostscript does and within 0.03
of it on ink coverage over a 32 by 32 grid; `table2_1_76.ps` 431x175 and `img.ps` 53x9, both of
which are a raster laid down with `image`, likewise; and `parrot.ps` 229x288 agrees with Ghostscript
on every pixel — the file's last operation covers the whole page in green, which Ghostscript also
draws, on a canvas of any size.

Text is not drawn, for the reason the SVG, HP-GL and DXF readers here do not draw it: the file names
a font and the glyphs are in the font. A page that is nothing but words comes out blank rather than
wrong.

`ai` up to version 8 is that same PostScript; from version 9 it is a PDF under an Illustrator name,
and its first four bytes send it to the PDF reader — checked by renaming a PDF to `.ai`, which comes
out the same picture under either name. That is the right reader rather than a complete answer: the
PDF reader here takes the rasters a document carries and does not draw a page's own content, so a
version 9 file whose artwork is vector has nothing in it for that reader to find. Drawing those
would be a PDF content-stream interpreter, which is a different job from this one.

What makes the name worth claiming is what it refuses. Eleven of the twelve `.ai` samples here declare procedure sets under
`%%DocumentNeededResources` and do not carry them — `Adobe_level2_AI5` and `Adobe_Illustrator_AI5`
for the ten written by Illustrator 6, `Adobe_packedarray` and `Adobe_IllustratorA_AI3` for
`DIAMONDS.AI` — and every operator their drawing is made of is defined in those sets. They are
refused by the names of the sets they are missing. Ghostscript refuses the same eleven, with
`undefined in Adobe_level2_AI5` and its equivalents, which is the same decision reached a little
later. `Illustrator8-s01.ai` carries its procedure sets and is drawn, at 723x1020, agreeing with
Ghostscript to 0.03 on the same measure.

An earlier attempt at `.ai` here rendered seven of eleven convincingly and four with whole figures
in a black none of them asked for, and was deleted rather than shipped because nothing could say
which four were wrong. The rule above is what says it: an operator that is not defined stops the
render with its name in the message, so a file that would have come out wrong comes out refused.

### The formats that had samples, read

  - `.b3d` is Maxon BodyPaint 3D, `AC4DBody` and then a tagged value stream with no length on any
    record. All ten distinct samples walk from the signature to the end of the file exactly with no
    tag unaccounted for, and every one of their 32,400 scanlines unpacks to exactly the width the
    header states. A second decoder written separately agrees with this one on every pixel of all
    ten. Files with a layer carry the same bitmap twice and one carries a layer with no pixels at
    all, so the picture taken is the first that has scanlines and is the size the header states.
  - `.cam` is a Casio QV camera. One container for both generations: four bytes, a count of areas,
    a descriptor each, and the areas end to end. The later cameras store a whole JFIF, which is
    handed over untouched; the QV-10 stores a JPEG with the markers, the frame and the Huffman
    tables taken out, and those are put back from `cam2jpgtab.h` in itojun's `qvplay`, which is the
    published reference. The quantisation tables are not reconstructed — only their segment headers
    are constant and the values come out of the file. All four reconstructions match ImageMagick's
    decode of the same stream exactly.
  - Reading them found a real fault in the JPEG decoder. A scan naming one component is not
    interleaved and its minimum coded unit is a single block, but the baseline decoder read every
    scan on the frame's interleaved grid — so a three-by-two picture had six blocks read where one
    was written and the bit reader was lost from the first block on. Nothing had noticed because
    every ordinary JPEG interleaves.
  - `.ssp` is an Axialis screensaver project and `.php` an Adobe PhotoParade album, and both hold
    several pictures. Neither is read by finding the first signature, which in every one of the
    nineteen samples would return a background tile, a theme backdrop or a thumbnail. The project
    states each picture's length immediately in front of it and that length has to be the one the
    picture's own framing gives; the album describes each photograph in a block standing directly
    behind it, so a photograph is the JPEG whose markers run out exactly where the next block
    begins. The album's own count of photographs agrees with the number of blocks in all seven.
  - `.sim` was reported as a small gap in the PC Paint reader. It was not: byte 10 was being read as
    a count of planes and byte 11 as a depth, where the format packs both into byte 10 and uses byte
    11 as a version flag of 0FFh — which is why the sample was refused for having a depth of 255.
    The two words behind that were being read as an aspect ratio the format has no field for, and
    the compressed data as bare count-and-value pairs where it is really blocks with their own
    headers and a marker byte saying which byte introduces a run. Read as written, the sample
    accounts for itself three times over and comes out as a legible line of text, the right way up
    once the rows are taken from the bottom of the picture upwards.

### Measured and left, so it need not be measured again

  - `.pp5` is Micrografx Picture Publisher 5 and it settles: `PPUBII`, a canvas size, then a chain
    of objects each of which is a 106-byte header, a length, and a little-endian TIFF whose
    compression tag 213 is plain zlib. Walking that consumes the one sample to the byte and every
    strip decompresses to exactly its own width times height times samples. The catch is that the
    base image is blank white and the picture is in four layers with 8-bit masks at stated origins,
    so a reader that returns the base image returns an empty rectangle — it needs compositing, and
    there is one sample to check that against.
  - `.92i` settles too, but it is not the container `TiPictureReader` already reads: the TI-92 has a
    directory of named entries at absolute offsets where the TI-82 and its siblings have a flat run.
    Both entries in the one sample are 127 by 63, plain 1bpp — the remark in `TiPictureFile` that
    these are compressed is wrong. `.73i` is the container already read here with a different width,
    but its picture type byte could not be confirmed from any source and there is no sample, so the
    `ti` row cannot be closed on `.92i` alone.
  - `.tex` under XnView's `pspt` name is a Paint Shop Pro texture, which none of the five samples
    here is. Four are Croteam Serious Engine textures and three of those settle exactly — the
    header's width and height are in world units and the pixel size is that shifted right by the
    first mip level, which reproduces every byte count. The fourth uses a `FRMC` block that does not
    appear in the released engine source at all, and the fifth is a `TDIPLOOM` document that cannot
    be shown to hold a raster.
  - `.ypc` is WhyPic, and its specification and reference source were found — the distribution's own
    `SAMPLE.YPC` is byte-identical to the corpus sample. It is not a header-and-unpack format: every
    byte past the fourth is arithmetic-coded against eight interpolated probability models. It is a
    project rather than a patch, and the reference source is GPL, so it would have to be written
    from the format document rather than ported.
  - `.frm` is not one format and mostly not pictures: sixteen of the samples are EZ-Forms form
    definitions, six are character-cell templates, one is an unidentified object format and one is a
    JPEG under the wrong name. There is no raster `.frm` here to implement.
  - `.lwf` is LuraWave. No description of its subband coding has ever been published and the
    decoder was licensed as a binary. This one is not worth further effort.

### The corpus was the limit, and it need not have been

Most of what is above was written as though the missing names had no samples. That was true of what
was on this machine and not true of what could be had: the sample archive `fetch-samples.sh` already
points at carries files for a third of them. Sweeping its 843 directories for the extensions listed
here returned **483 files across 63 of the missing extensions** — `gem`, `fif`, `frm`, `mix`, `wzl`,
`cat`, `svg`, `eri`, `dwg`, `prf`, `lwi`, `cgm`, `ssp`, `ai`, `afx`, `wi`, `pdd`, `jig`, `sid`,
`pxa`, `pst`, `b3d`, `tile`, `ibg`, `xar`, `crw`, `k25`, `mrw`, `x3f` and more, several of them with
a dozen or more samples each.

Several samples of one format is worth more than one: a field constant across all of them is
structure, and one that varies with the picture is a dimension. That is a stronger position than
most of the formats already read here were settled from.

Of the 63, eight or so are vector — `svg`, `ai`, `ps`, `cgm`, `dwg`, `hpgl`, `xar`, `gem` — and want
a rasteriser rather than a layout. There is one now, in `FileFormat.Core/Vector`: a path buffer, a
scanline filler with both winding rules, a stroker, a stipple, a clip mask and a gradient paint.
Six of the eight are read through it or past it, and the reasoning about size is the same for all
of them — the file's own stated page, and nothing invented where it states one.

  - `gem` reads all 42 samples. Every one walks from the header to the terminating word and lands
    on it exactly. The size is the header's arithmetic: the extent as a fraction of the coordinate
    window, times the page in tenths of a millimetre, at 96 pixels to the inch.
  - `svg` reads 10 of its 14. Sizes match librsvg exactly where it renders at all, and by eye the
    geometry does too. Three are refused as malformed XML — an undeclared `xlink` prefix, a
    redefined `xmlns`, and a file that is not XML at all — and librsvg refuses all three for the
    same reasons. One more states a height of zero, which librsvg also calls sizeless.
  - `cgm` reads the six binary samples and refuses the character and clear-text encodings, which
    are different grammars rather than variants. `abydos` is in the corpus as a metafile, an SVG
    and a plot; ours of the metafile against librsvg's of the SVG is 1.3% RMSE at 512x384.
  - `hpgl` reads all five. Nothing here renders HP-GL, so the evidence is the parse: a second
    reading written separately agrees on the extent of all five.
  - `xar` and `dwg` are not rendered and should not be. Both state where a preview lives — Xara in
    a record tagged 61, 62 or 63 by picture format, AutoCAD at the address in byte 13, behind a
    sentinel and its own complement — and all nine previews match ImageMagick on every pixel.

`ai` and `ps` are the two left, and `ai` was written and then removed rather than shipped. The
imaging model is a closed grammar Adobe published, the bounding box gives the size, and seven of
the eleven samples came out looking right. The other four came out with whole figures in black,
which is a colour the file never asks for — every path in them sets its own CMYK. What made it
unfixable inside this piece of work is that there is nothing here to check against: Ghostscript
refuses ten of the eleven outright, because they call for the `Adobe_level2_AI5` procset under
`%%DocumentNeededResources` and do not carry it. A reader that draws four plausible wrong pictures
out of eleven, with no way to tell which four, is the thing this file has twice been rewritten to
avoid. What was learnt and is worth not learning again: the case of a path operator says smooth or
corner and not absolute or relative; `v` omits the first control point and `y` the second; and a
compound path between `*u` and `*U` is painted once at the end rather than per subpath.

### And where the ceiling actually is

The archive was then swept a second time for every remaining name, reading up to two hundred entries
a directory rather than sixty, across all 843 of them. It returned **one file**. So the ninety-odd
names still without a sample are not waiting on a more patient search of that archive: it does not
have them.

Two things make this harder than it sounds. Most files there are stored under a name with no
extension at all — `beaker03`, `caligpen` — so matching by extension finds only what happens to be
named for its format. And matching by directory name instead is worse than useless: "tdi" is a
substring of "artDirector", and a dozen such coincidences look exactly like hits until the samples
are opened.

Other public corpora were looked at — the Open Preservation format corpus, the codec conformance
suites — and they cover mainstream codecs thoroughly and none of these names at all.

So for those ninety-odd the honest position is that there is nothing here to check an implementation
against, and this project's standard is that a reader agreeing with nothing but itself is worth less
than no reader. Several have been shown wrong by exactly that route already. What would move them is
a sample or a specification, not more effort.

The last column marks the ones XnView itself cannot load on this platform: its catalogue says
Windows only, so nothing here has ever been able to compare against them either.

| XnView name | extensions | |
|---|---|---|
| 2d | .2d |  |
| abs | .abs |  |
| afx | .afx |  |
| aim | .ima |  |
| ami | .[b] |  |
| anv | .anv |  |
| aphp | .php | every photograph read, not the theme artwork |
| apx | .apx |  |
| arf | .arf |  |
| arn | .arn |  |
| aurora | .sim | read; it is a Pictor page and the reader of those was wrong |
| avs | .mbfavs .mbfs .x | read |
| b3d | .b3d | read |
| bfli | .flp |  |
| bias | .flt .msk |  |
| bif | .bif |  |
| bmc | .bmc |  |
| bmf | .bmf | Windows only |
| bmg | .bmg .ibg |  |
| bms | .bms |  |
| bpr | .bpr |  |
| btn | .btn |  |
| bum | .bum |  |
| cam | .cam | read, both camera generations |
| car | .car |  |
| cat | .cat |  |
| cbmf | .bmf |  |
| cdr | .cdr |  |
| cft | .ctf |  |
| cgm | .cgm | binary encoding read; character and clear-text refused |
| cloe | .cloe | read |
| cmt | .cmt |  |
| cmx | .cmx |  |
| cncd | .ncd |  |
| crd | .crd |  |
| crw | .crw |  |
| cvp | .cvp |  |
| d3d | .b2d .b3d |  |
| dsi | .dsi |  |
| dwg | .dwg | thumbnail at the stated address |
| dxf | .dxf | read from the published group codes; Windows only in XnView |
| ecc | .ecc |  |
| eidi | .ei .eidi |  |
| eif | .eif |  |
| eri | .eri | Windows only |
| fbm | .cbm |  |
| fff | .fff |  |
| fi | .fi |  |
| fif | .fif | Windows only |
| fre | .fre |  |
| frm | .frm |  |
| frm2 | .frm |  |
| fsy | .fsy |  |
| fx3 | .fx3 |  |
| gem | .gem | read |
| gm | .gm .gm2 .gm4 |  |
| hdri | .hdri |  |
| hdru | .gn .hdru |  |
| hpgl | .hgl .hpg .hpgl .prn .prt | read |
| hru | .hru |  |
| hta | .hta |  |
| icd | .idc |  |
| icon | .pr |  |
| iff | .blk | read |
| iimg | .iimg |  |
| imi | .imi |  |
| imt | .imt |  |
| ioca | .mod |  |
| ipg | .ipg |  |
| iss | .iss |  |
| iwc | .iwc | Windows only |
| jbf | .jbf | version 2 read; version 1's bitmap coding refused |
| jig | .jig |  |
| jig2 | .jig |  |
| k25 | .k25 |  |
| kps | .kps |  |
| kqp | .kqp |  |
| kskn | .thb |  |
| lda | .lda |  |
| ldf | .ldf | Windows only |
| lvp | .lvp |  |
| lwf | .lwf | Windows only |
| lwi | .lwi |  |
| mbig | .big |  |
| mdl | .mdl |  |
| mfrm | .frm |  |
| mix | .mix |  |
| mjpg | .wi |  |
| mph | .mph |  |
| mrf | .mrf |  |
| mrw | .mrw |  |
| mtx | .mtx |  |
| ncr | .ncr |  |
| ncy | .ncy |  |
| nsr | .bn .ph |  |
| oil | .oil |  |
| pan | .pan |  |
| pax | .pax |  |
| pbt | .pbt |  |
| pcl | .pcl | the raster subset read; text and HP-GL/2 passed over |
| pd | .pd .t1 .t2 |  |
| pdd | .pdd |  |
| pdx | .pdx |  |
| pegs | .pxa .pxs |  |
| pig | .pig |  |
| pixi | .pxb |  |
| pixp | .i17 .i18 .ib7 .if9 |  |
| pmp | .pmp |  |
| pmsk | .msk | read by the Paint Shop Pro reader |
| pp4 | .pp4 |  |
| pp5 | .pp5 |  |
| pps | .pps |  |
| ppt | .ppt |  |
| prc | .prc |  |
| prf | .prf |  |
| prisms | .pri |  |
| pseg | .pse |  |
| pspb | .pspbrush |  |
| pspf | .pfr .pspframe | read by the Paint Shop Pro reader |
| pspm | .pspmask |  |
| pspt | .tex | read by the Paint Shop Pro reader |
| pwc | .pwc | Windows only |
| pxa | .pxa |  |
| pzl | .pzl |  |
| pzp | .pzp |  |
| qcad | .cad |  |
| raw | .grey .gry |  |
| rfax | .001 |  |
| rix | .sc? |  |
| sct | .ch | read by the Scitex CT reader |
| sdg | .sdg |  |
| sfax | .001 |  |
| sid | .sid | Windows only |
| skf | .skf |  |
| skn | .skn |  |
| smp | .smp |  |
| ssi | .ssi |  |
| ssp | .ssp | every embedded picture read, not the first |
| stm | .stm |  |
| stw | .stw |  |
| svg | .svg | read |
| synu | .syn .synu |  |
| taac | .suniff .taac .vff | read, and checked against the sample |
| tdi | .tdi |  |
| tdim | .tdim |  |
| ti | .73i .82i .83i .85i .86i .92i |  |
| tile | .tile |  |
| tjp | .tjp |  |
| tnl | .tnl |  |
| tsk | .tsk |  |
| ttf | .ttf | drawn as a sheet of its glyphs |
| tub | .psptube .tub |  |
| upe4 | .pe4 |  |
| upi | .upi |  |
| upst | .pst |  |
| uyvy | .qtl |  |
| uyvyi | .qtl |  |
| vit | .vit |  |
| vob | .vob |  |
| wic | .wic | Windows only |
| wrl | .wrl |  |
| wzl | .wzl |  |
| x3f | .x3f |  |
| xar | .xar | preview at the stated tag |
| xif | .xif | a TIFF; the sample's private compression 34673 is not decoded |
| xim | .xim |  |
| xp0 | .xp0 |  |
| ypc | .ypc | Windows only |
| yuv411 | .qtl |  |
| yuv422 | .qtl |  |
| yuv444 | .qtl |  |
| zbr | .zbr |  |
| zmf | .zmf |  |
