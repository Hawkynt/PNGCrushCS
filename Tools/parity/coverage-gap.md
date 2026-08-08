# What we do not read that XnView says it does

Generated from XnView's own `Formats.txt` against `Decode --extensions`. RECOIL's catalogue is
covered but for `.gr10p`, which RECOIL comments out of its own list for having a five-character
extension, so this is the whole of the known coverage gap.

A name here is a file we cannot open. That is counted and closed rather than explained — unlike
the rendering differences in the report beside this, which are cases of the tool giving
something up and are correct as they stand.

**197 distinct extensions across 176 of its format names** when this was written. A few extensions
are claimed by more than one of its names, so the rows below add up to more than that.

**Forty are closed now and 136 remain.** Eight of the fifteen turned out to be one thing — a
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
| ai | .ai |  |
| aim | .ima |  |
| ami | .[b] |  |
| anv | .anv |  |
| aphp | .php |  |
| apx | .apx |  |
| arf | .arf |  |
| arn | .arn |  |
| aurora | .sim |  |
| avs | .mbfavs .mbfs .x |  |
| b3d | .b3d |  |
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
| cam | .cam |  |
| car | .car |  |
| cat | .cat |  |
| cbmf | .bmf |  |
| cdr | .cdr |  |
| cft | .ctf |  |
| cgm | .cgm | binary encoding read; character and clear-text refused |
| cloe | .cloe |  |
| cmt | .cmt |  |
| cmx | .cmx |  |
| cncd | .ncd |  |
| crd | .crd |  |
| crw | .crw |  |
| cvp | .cvp |  |
| d3d | .b2d .b3d |  |
| dsi | .dsi |  |
| dwg | .dwg | thumbnail at the stated address |
| dxf | .dxf | Windows only |
| ecc | .ecc |  |
| eidi | .ei .eidi |  |
| eif | .eif |  |
| eps | .ps |  |
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
| iff | .blk |  |
| iimg | .iimg |  |
| imi | .imi |  |
| imt | .imt |  |
| ioca | .mod |  |
| ipg | .ipg |  |
| iss | .iss |  |
| iwc | .iwc | Windows only |
| jbf | .jbf |  |
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
| pcl | .pcl |  |
| pd | .pd .t1 .t2 |  |
| pdd | .pdd |  |
| pdx | .pdx |  |
| pegs | .pxa .pxs |  |
| pig | .pig |  |
| pixi | .pxb |  |
| pixp | .i17 .i18 .ib7 .if9 |  |
| pmp | .pmp |  |
| pmsk | .msk |  |
| pp4 | .pp4 |  |
| pp5 | .pp5 |  |
| pps | .pps |  |
| ppt | .ppt |  |
| prc | .prc |  |
| prf | .prf |  |
| prisms | .pri |  |
| ps | .prn .ps .ps1 .ps2 .ps3 |  |
| pseg | .pse |  |
| pspb | .pspbrush |  |
| pspf | .pfr .pspframe |  |
| pspm | .pspmask |  |
| pspt | .tex |  |
| pwc | .pwc | Windows only |
| pxa | .pxa |  |
| pzl | .pzl |  |
| pzp | .pzp |  |
| qcad | .cad |  |
| raw | .grey .gry |  |
| rfax | .001 |  |
| rix | .sc? |  |
| sct | .ch |  |
| sdg | .sdg |  |
| sfax | .001 |  |
| sid | .sid | Windows only |
| skf | .skf |  |
| skn | .skn |  |
| smp | .smp |  |
| ssi | .ssi |  |
| ssp | .ssp |  |
| stm | .stm |  |
| stw | .stw |  |
| svg | .svg | read |
| synu | .syn .synu |  |
| taac | .suniff .taac .vff |  |
| tdi | .tdi |  |
| tdim | .tdim |  |
| ti | .73i .82i .83i .85i .86i .92i |  |
| tile | .tile |  |
| tjp | .tjp |  |
| tnl | .tnl |  |
| tsk | .tsk |  |
| ttf | .ttf |  |
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
| xif | .xif |  |
| xim | .xim |  |
| xp0 | .xp0 |  |
| ypc | .ypc | Windows only |
| yuv411 | .qtl |  |
| yuv422 | .qtl |  |
| yuv444 | .qtl |  |
| zbr | .zbr |  |
| zmf | .zmf |  |
