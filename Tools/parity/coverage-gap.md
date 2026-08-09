# What we do not read that XnView says it does

Generated from XnView's own `Formats.txt` against `Decode --extensions`. RECOIL's catalogue is
covered but for `.gr10p`, which RECOIL comments out of its own list for having a five-character
extension, so this is the whole of the known coverage gap.

A name here is a file we cannot open. That is counted and closed rather than explained — unlike
the rendering differences in the report beside this, which are cases of the tool giving
something up and are correct as they stand.

**197 distinct extensions across 176 of its format names** when this was written. A few extensions
are claimed by more than one of its names, so the rows below add up to more than that.

**A hundred and fifty-five are closed now and 26 remain.** The last six went together and none of them
needed a corpus: `pps`, `ppt` and `tsk` were read out of the converter's own code and checked by
building files for it, and `ami`, `rix` and `wrl` were three rows the catalogue had never really
opened — two whose extension was a bracket or a wildcard rather than a name, and one whose reader
does not exist. Eleven before them went in one pass, off a source
that had been sitting in this tree unread — see "Reading the reader" below. Eight of the fifteen
before them turned out to be one thing — a
**A hundred and nine are closed now and 69 remain.** Eight of the fifteen turned out to be one thing — a
**A hundred and twenty-three are closed now and 55 remain.** Eight of the fifteen turned out to be one thing — a
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
    is refused rather than drawn. `.msk` was claimed for the wrong reader and is corrected below.
  - `avs` wants `.mbfavs` and `.mbfs`. Claimed: an AVS raster has no signature at all, but its two
    lengths have to account for the file to the byte, which a foreign file does not do.
  - `hpgl` wants `.prn` and `.prt`, the names a driver gives a job printed to a file. Claimed
    because the parse decides, not the name: it requires an instruction that moves the pen and says
    where to, and all five PostScript samples here are refused by it. That test was not enough and
    the audit below says what replaced it.
  - `cloe` wants `.cloe`, which is the long name of the format read here as `.clo`. Claimed, after
    taking out of the reader the part that invented 320 by 200 whenever the header stated no size —
    which meant any file long enough was drawn as a picture of a size it never claimed.

Declined, and why:

  - `bfli` wants `.flp`. The BFLI reader validated nothing but the file's length, and it could be
    shown to: handed the IOCA sample in this corpus it reported a 320 by 200 picture. Claiming
    `.flp` would have drawn whatever was under that name. It requires the load address now, so that
    objection is answered — and the name is still declined, for a second one recorded below.
  - `ioca` wants `.mod`. Worse — the IOCA reader falls back to reading the first four bytes of
    anything at all as a width and a height. `.mod` is an Amiga music module as often as anything
    else, and every one of them would be drawn.
  - `eps` wanted `.ps`, and was declined here for a while: the EPS reader reads the DOS binary
    wrapper and the TIFF preview inside it, and plain PostScript is a language rather than a layout.
    That is no longer the position — there is an interpreter now, and the entry below says what it
    does.

Three more were looked at and left: `aim` wants `.ima`, but the reader's "AIM\0" signature is not
sourced from anything and there is no sample, so the claim could not be shown to read a real file —
and it is now known where that signature came from, which is nowhere; `icd` wants `.idc` and `pixi`
wants `.pxb`, and neither is the format the similarly-named reader here actually reads, which for
`icd` was since confirmed by XnView refusing the icdraw samples under that name.

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

### The converter itself turned out to be the oracle

Everything above was written as though XnView could only be compared against on files we already
had. That was wrong twice over, and correcting it closed fifteen more names.

The first correction is that **XnView's own converter reads almost all of them here**. `nconvert`'s
built-in list on this platform names 540 formats, and 36 of the 38 taken in this pass are in it —
only `iwc` and `ldf`, the two its catalogue marks as needing a Windows plugin, are absent. So for
every one of the other 36 there is a tool on this machine that reads the format, prints the size,
the depth and the number of components it finds, and converts the picture out. That is an oracle,
and it can be interrogated rather than merely consulted:

  - **A file built to a hypothesis and handed to it says whether the hypothesis is right.** It
    reports the width, the height and the depth it read, so a layout can be proposed, built, and
    corrected until every field comes back as the one written — and then the picture converted out
    can be compared, byte for byte, against the pixels that went in. That is how `mtx`, `car`,
    `bms`, `anv`, `arf`, `hdru` and `imi` were settled, none of which has a sample anywhere.
  - **Inverting one byte of a real file at a time and asking again says exactly where the signature
    is.** Doing that to the Eroiica sample showed that inverting any of the first eight bytes makes
    it stop recognising the file and that inverting any of the next sixteen does not, which is a
    stronger statement of a magic number than a specification usually gives.

The second correction is that **the sample archive can be browsed by format name rather than swept
by extension**. `telparia.com/fileFormatSamples/image/` is a directory per format, 844 of them, and
the names are the ones dexvert uses rather than the extensions XnView uses — which is why sweeping
for extensions found so little. Two of this pass's names were sitting there: eighteen ElectricImage
files and thirteen Amica Paint ones.

What that produced, all of it checked against `nconvert` on every pixel:

  - `.fre` is XnView's "Male Normal CT" and it is a **GE Genesis 5.x image**, the format the scanner
    console wrote, met under the extension the Visible Human male's CT slices carry. Its layout is
    published, in part 4 of David Clunie's Medical Image Format FAQ: `IMGF`, then big-endian words
    giving the displacement to the pixels, the width, the height, the depth and a compression code.
    Only the uncompressed case is read, and what says a file is uncompressed is the arithmetic
    rather than the code — the one file measured states 1 where the FAQ's list puts "as is" at 0,
    and a compressed one cannot account for the rest of the file to the byte. Sixteen-bit samples
    are scaled by the picture's own largest sample, because a CT slice uses a few thousand of the
    65,536 levels and would otherwise come out black; scaling to 255 whole levels and 256 sublevels
    of each rather than to 65,535 makes the top byte of every sample exactly the eight-bit picture
    XnView draws. All 262,144 pixels agree.
  - `.eif` is **Eroiica**, an engineering-drawing viewer, and its file is a compound document rather
    than a picture: a set description, a page list, text, and the scans. The scans are whole TIFF
    streams standing inside it, each complete with its own byte-order mark and its own offsets, and
    a page is found by looking for one and then requiring it to account for itself — the directory
    has to parse, every entry's value has to stand inside the file, and the strips have to end
    inside it. Breaking the first stream's byte-order mark makes XnView report the second one's size
    instead, which is what says it scans for them too. The one sample carries five: a 259x197 colour
    illustration and four 2068x1581 Group 4 scans of the drawing. Ours of the illustration, XnView's
    of the whole document, and ImageMagick's of the stream cut out of it are the same picture on
    every byte.
  - `.ei` and `.eidi` are **ElectricImage**, what the Animation System wrote its renders as. NihAV
    carries the layout and eighteen real files confirm it: every one walks from the header to its
    own last byte exactly, and every one's run-length data unpacks to exactly the width times the
    height the header states while consuming exactly the bytes it said it would. One correction the
    eighteen forced: five bytes stand behind the header in mode 1 and none in mode 0x0100, and with
    five skipped for both the two eight-bit files overran their ends by exactly five. All eighteen
    decode identically to XnView — the colour table for the eight-bit ones, red, green and blue for
    the one true 24-bit one, and alpha first for the fifteen with a fourth channel.
  - `.mtx` is **Maw-Ware Textures**: a constant 0x69, the width, the height, how many bytes a pixel
    takes and a word that is read and ignored, then the pixels. One byte a pixel is a grey, three
    are red, green and blue, four are those with a fourth XnView drops; all three come back byte for
    byte. Two bytes a pixel is refused — XnView accepts it and calls it sixteen bits, but what it
    converts out bears no relation to what goes in under any obvious reading. The file has to be
    exactly as long as its header says, which is stricter than XnView and is what stands in for a
    signature four bytes long.
  - `.car` is a **NeoBook cartoon**: the letters `SN`, a 32-bit offset, and a PNG standing at exactly
    that offset. TrID's definition, built from three real files, records the same thing from the
    other end. Moving the picture while leaving the offset alone is refused, as is a JPEG in the
    PNG's place.
  - `.bms` is a **playback bitmap sequence**: ten letters reading `BMSWinPlay`, six bytes, and a
    Windows bitmap. What XnView converts out of it is what it converts out of the same bitmap
    standing alone.
  - `.anv` is **AirNav**, which is a 256-colour Windows bitmap with `AN` written over the `BM`. The
    reader does not read the bitmap's own fields: it takes the width and height from where a bitmap
    keeps them and then goes to fixed places — the colour table at 54 and the picture at 1078 — so
    that is what is read here, with the file additionally having to describe itself as the 256-colour
    bitmap it is before it is read at offsets it never meant.
  - `.arf` is not the one with a specification. Axon's Raw Format, which INDEC BioSystems' Imaging
    Workbench writes, is fully described in its application note and opens with a byte-order word and
    the letters `AR` — and XnView refuses a file built to that note. What XnView's ARF reader wants is
    `BB BB BA AD`, a version of 2, and a type code of 0, 1 or 2, with eight bits a pixel at a stated
    offset. Nothing published describes that at all.
  - `.hdru` and `.gn` are **Apollo HDRU**, which nothing ties to Apollo Computer: sixteen big-endian
    bytes opening `01 01`, a compression code, a resolution, the width and the height, then one bit a
    pixel with a set bit white — measured, since XnView's portable bitmap of the file is its bytes
    inverted and a portable bitmap is one-for-black. Only the uncompressed case is read; where a
    Group 3 or Group 4 stream begins is not visible from the header and no file of either kind could
    be had.
  - `.imi` is **TMSat**, the Thai-Paht satellite's camera, and it has no header at all: the files are
    the samples and the name says which camera and band. So one length is taken, 1,040,400 bytes,
    which is the narrow-angle camera's 1020 by 1020 and is the only length XnView takes either — it
    refuses the wide-angle camera's 352,192 and refuses one byte less than 1,040,400.

Four more were names for a reader already here, claimed on the same test as the earlier seven — the
reader has to refuse a foreign file arriving under the name, and each was checked by forcing XnView's
reader for that name to take a JPEG, which every one of them refused:

  - `kskn` wants `.thb`, `2d` wants `.2d` and `bmc` wants `.bmc`. All three of XnView's readers for
    those names are its Windows bitmap reader, and a bitmap renamed to any of them is reported as a
    Windows Bitmap of the right size. Claimed. What those names are elsewhere is not this and is not
    a picture: Amapi's own drawings are `.a3d` three-dimensional geometry, and `.bmc` in the
    embroidery world is a cache of stitches that libembroidery registers as stitch-only and refuses
    to read for want of any description of it. Neither opens with `BM`, so neither is drawn.
  - `fx3` wants `.fx3`, Fugawi's packaged raster chart. XnView's Fugawi reader is a TIFF reader —
    it takes a TIFF renamed `.fx3` and refuses a JPEG — and Blue Marble's Global Mapper developer
    says of a real one that it "is a TIFF image" whose positioning is not stored the GeoTIFF way.
    Claimed. The calibration is in private tags nothing here reads and nothing published describes.
  - `ami` wants `.[b]`, and that is not an extension. XnView's catalogue lists a format's extensions
    in one column and the script that built this table took the second token for one; the thirteen
    Amica Paint files in the sample archive are named `[b]64'er.ami`, `[b]kugel.ami` and so on, so
    `[b]` is the prefix the Commodore scene put on a bitmap taken off a disk image and the extension
    is `.ami`, which is already read here. The row is a cataloguing artefact rather than a format.

### Measured and left, second pass

  - `aim` wants `.ima`, and the reader here that carries the name is built on a signature that does
    not exist. There is no `AIM\0` anywhere in XnView's reader: the four bytes appear only as the
    label it writes into its own info structure. The real files have no header at all. XnView opens a
    companion `<name>.hd` beside the picture and takes a big-endian `AA` at offset 4, a width at 22
    and a height at 24 from it, requiring the width times the height to be the picture file's exact
    length; failing that it accepts a file of exactly 65,536 bytes as 256 by 256. Declined on the
    second of those, which is the `ioca` objection in another form — any 64K file under the name
    would be drawn — and the first cannot be reached from an interface that is handed bytes. The
    `.aim` extension and the invented magic that this library already claims should go.
  - `bfli` wants `.flp`, and it is declined again, for a new reason. The reader now requires the load
    address, so it would refuse a foreign file, but what it draws is wrong: it renders 320 by 200 in
    hires where the format is 320 by 400 in two multicolour FLI frames. RECOIL requires the file to
    be exactly 33,795 bytes with `b` at offset 2 and draws it at 320 by 400, XnView reports 320 by
    400, and all three samples here are exactly 33,795 bytes. Claiming a name for a reader that
    draws the wrong picture is worse than leaving the name. The reader wants correcting first.
  - `mdl` wants `.mdl`, the Half-Life model. Declined: `HalfLifeMdlReader` is not a reader of that
    file at all. It takes a bare `.mdltex` blob, reads a width and a height out of the first bytes
    with no signature anywhere, and accepts anything long enough. A real model opens with `IDST` and
    keeps its skins in a table the header points at, and none of that is here.
  - `apx` is Ability Photopaint. Two files gathered under the name open with `SD3S` and are a tagged
    stream — `MXRB`, `MXLS`, a layer name, an author credit, and the size at offset 10 as two
    big-endian words. XnView's own `apx` reader refuses both, so either they are not the format or
    the reader on this platform cannot take them, and there is nothing to settle which.
  - `hta` is Hemera Thumbs, and it is the one case where two implementations disagree. Deark has a
    module for it: `89 48 54 41 0D 0A 1A 0A`, a version that has to be 100, a count, then a
    directory of position and length pairs, each entry a whole picture file. A file built to that is
    read by deark and its pictures come out — and XnView refuses it. So one of the two is reading
    something the other is not, and with no real file to decide it was left.
  - `abs` is Optocat, the acquisition software Breuckmann shipped with its 3D scanners, and the scan
    it stores is a raster. Its layout was recovered from XnView's reader and confirmed by
    construction: `II` or `MM` for the byte order, a 16-bit offset to the pixels at 4 that has to be
    over 2047, samples per pixel at 10 giving 8, 16, 24 or 32 bits, the width at 14 and the height at
    16, then uncompressed rows. Not implemented here — `II`/`MM` at the front is TIFF's own opening
    and the rest is 16-bit fields with no constant anywhere, which is too little to tell a scan from
    a TIFF under the wrong name.
  - `arn` is the Astronomical Research Network, and it is a NASA PDS-style label: the six letters
    `SIMPLE`, whose value has to begin `T  / ARN PROVISION`, then `RECORD_BYTES`, `LABEL_RECORDS`,
    `LINES`, `LINE_SAMPLES` and `SAMPLE_BITS` as keyword-equals-value lines, with the picture
    beginning at the record size times the record count. Recovered from XnView's reader and confirmed
    by writing a label by hand. Not implemented here for want of time rather than evidence; the
    18-character string is as good a signature as any in this file.
  - `iwc` is WaveL's wavelet codec, and its header is fully recovered — from WaveL's own Java decoder
    applet, whose class fields survived: `IWC`, a version byte, the two sizes, a level count and a
    quality, then float scale factors and minima, nine floats per level, and a byte count that
    matches the payload of the sample exactly. What is not recovered is the coding, which is an
    entropy-coded subband scheme that would have to be written out of decompiled Java. XnView cannot
    read it on this platform either.
  - `mbig` is the Michelin road atlas, and it has no magic bytes at all: four 32-bit words giving a
    tile size and a grid, then a directory of two words per tile where a second word of zero means
    the tile is absent, and the picture's size is the occupied part of the grid times the tile. That
    much was recovered and confirmed by construction. How a tile's pixels are stored was not, and a
    format identified by nothing but four numbers in range is the `ioca` objection again.
  - `gm`, `gm2` and `gm4` are **Autologic** typesetter rasters — Image Alchemy's manual says mode 2 is
    black and white and mode 4 is grey, and that only the High Speed Interface inline form is read
    and only behind a Graphics Parameter Block. Autologic's own *Input Command Language* manual
    documents the graphics header and the byte-pair line-art coding, but TrID's definition, built from
    twelve real files, records `FF 04 00 07` at the front, which is not that header's first field. The
    published document and the real files cannot be reconciled without one of the files.
  - `cmt` is the Chinon ES-1000, and its reference implementation is public and in the clear: YOSHIDA
    Hideki's `cmttoppm.c`, "COMET" and a 128-byte header, then a 512-byte camera header, then 512 by
    243 raw CCD bytes. What it does with them is three passes of interpolation, a saturation, a
    histogram normalisation and a gamma, and none of that can be checked against anything: no `.cmt`
    file could be found anywhere, and a reader whose only agreement is with its own arithmetic is
    what this file exists to prevent.
  - `dsi` is Cimage, an engineering document-management raster, Group 4 compressed — attested by two
    viewer vendors whose format lists are Rasterex's rather than XnView's, so independently of the
    catalogue this table is built from. No byte-level description exists.
  - `imt` is IMNET, a healthcare document and microfilm archive system, and Accusoft's ImageGear
    describes it as one bit a pixel with Group 3 and Group 4 coding and its own autodetection. No
    layout published. Python's `ImtImagePlugin` is a different format entirely — IM Tools, a
    plain-text header ending in a form feed — and is not this.
  - `lda` is LaserView, by LaserData of Cambridge, Massachusetts. LEADTOOLS calls it "a legacy Group 4
    compressed FAX format" and Accusoft ships the same thing as `.lv`; three vendors describe one
    format under three default extensions. No layout published, and dexvert lists XnView's detection
    of it among the ones it does not trust.
  - `icd` wants `.idc`, and it is not the icdraw format the similarly-named reader here reads — XnView
    refuses the icdraw samples under it. It is CORE Software Technology's, of Pasadena: a
    multi-band remote-sensing raster, one to sixteen bits a channel, whose one third-party reader was
    Image Alchemy under an OEM arrangement. No layout published.
  - `cft` wants `.ctf`. OptiGraphics Corporation of Grand Prairie, Texas made lenticular prints —
    Cracker Jack prizes, Slurpee coins, the 1986 Sportflics cards — so the format is plausibly an
    interlaced source image, and XnView's neighbouring `ttf` name is "Optigraphics Tiled". That is
    inference from what the company sold and not from anything about the file. No spec, no sample.
  - `ldf` is LuraDocument, LuraTech's mixed-raster-content scheme and the ancestor of their JPEG 2000
    Part 6 work. Binary-only licensed, like LuraWave beside it in this table. Not worth further
    effort.

### Nothing credible describes these

`bpr`, `crd`, `cvp`, `fff`, `iss` and `bif` were searched for and are not described anywhere.

  - `bpr` is XnView's "AAA logo". The current AAA Logo, by SWGSoft, never mentions `.bpr`; the
    extension does not appear in its installer's strings. TrID's two `.bpr` definitions are a C++
    Builder project and a Baltie program. Whether XnView's name even refers to that product is
    unestablished.
  - `crd` is "PowerCard maker", of which no trace exists outside XnView's list. TrID's four `.crd`
    definitions are Windows Cardfile, SoftKey Greeting Card Designer, a PPC organiser and HTC
    firmware. XnView's binary carries the string `CRD : No images` beside the same message for the
    project and album formats, which suggests a card document it lifts pictures out of rather than a
    bitmap, and the literal `CardMaker` stands next to it; files built with that at the front were
    all refused.
  - `cvp` is "Portrait". No product, no spec, no sample. The only `.cvp` definition anywhere is
    TrID's WinFax cover page, which is a different format and should not be assumed to be this one.
  - `fff` is "Maggi Hairstyles & Cosmetics", which is real — a hairstyle simulator by SOLution
    LABoratory, on several shareware directories — but nothing describes its file. XnView's binary
    carries the lowercase string `hairstyles & cosmetic ` where other formats keep their magic
    strings, and files built with it at the front were refused, so it is a lead and not a finding.
  - `iss` is listed by XnView as nothing but `ISS`, and has been since at least its 2005 build. It is
    in none of the imaging SDKs' format lists, in no wiki, in no magic database. Its neighbours in
    XnView's own list are 1990s enterprise document-imaging formats, which is a guess and is recorded
    as one.
  - `bif` is byLight Technologies' 20/20, a Windows capture-and-annotate program, and BIF was new in
    version 2.2 around 2000. The manual is archived and says only that BIF is multi-page. TrID, from
    ten real files, records `FA BA` at the front and `04` at offset 3, and XnView requires the same
    two bytes and then skips 372; a file built to that alone is refused, so there is more structure
    that could not be reconstructed without one of the ten.

Searches for all six ran through DuckDuckGo, Mojeek and Bing, the file-format wiki and its digipres
mirror, the Encyclopedia of Graphics File Formats' full CD text, deark, dexvert, TrID's 21,965
definitions, `file`'s magic database, ImageMagick's coder list and telparia's 844-format sample
archive. The consumer extension directories — filext, file.org, openwith, datatypes, filesuffix and
the rest — return only reworded copies of XnView's own one-line description, several now padded with
invented detail, and none of it was treated as evidence.
### Two things that were here all along

The names in the left-hand column are XnView's, and its catalogue says what each of them is. That
was never read: `Formats.txt` has a description column beside the extension column, and the list
above was generated from the extensions alone. Reading the other column names every row outright.
Half a day of searching for what a `pixp` or an `ssi` might be was work that a file already on this
machine would have finished in a minute, and the names it gives are not guessable — `oil` is not
Micro Illustrator but the Open Image Library, `tdi` is not Art Director but Explore, `wic` is not
Microsoft's imaging component but a wavelet codec of the same initials, and `ncr` is a scanner
company rather than an encrypted JPEG. Three of those four had been guessed wrong here.

The second is that `nconvert`, XnView's own converter, is in this tree and runs on this platform.
That changes what "no sample" means, in two ways.

It **writes** six of the missing formats — `prc`, `raw`, `tdi`, `uyvy`, `uyvyi` and `wrl` — so a
sample of those can be made rather than found, from a picture of our own choosing, which is better
evidence than a found sample because the original is known.

And for the ones it only reads, a file built to a published specification can be handed to it. If
XnView reads a file this project constructed at the size and depth it was built with, two
independent readings of the same document agree, which is most of what a real sample would have
settled. That is not the same as agreeing on pixels — it says the header was read the same way —
and where it can be pushed further it has been.

The rest of this section is what those two things settled.

### The five names on one extension, and the sixth beside them

`uyvy`, `uyvyi`, `yuv411`, `yuv422` and `yuv444` all want `.qtl`, and `raw` wants `.grey` and
`.gry`. The layout each name describes is fully determined by the name; what is not determined is
how big the picture is, and that is the whole question.

Nothing states it. `nconvert` writing an eight by eight picture as `uyvy` produces 128 bytes and no
header — the pixels and nothing else — and handed that file straight back it answers *Don't know how
to read this picture*, with or without the `-size 8x8` its own help offers for exactly this case.
The tool that wrote the file cannot read the file. Its help is the evidence rather than the failure:
`-size geometry : Width and height (Raw/YUV)` says in as many words that for these formats the
operator supplies the size because the file does not.

`.qtl` makes it worse rather than better. Five of the six names claim that one extension, with five
mutually incompatible pixel layouts, so even a reader that knew the size could not know which of the
five to apply — and `.qtl` is separately registered as QuickTime Media Link, which is an XML
playlist and not pixels at all.

So there is nothing to implement. A reader that picked a size from the file's length would draw a
sheared picture whenever it picked wrong and would have no way of knowing it had. The row stays open
and states why, which is a better answer than a reader that is right about one frame size in ten.

`.raw` and `.uyvy` are already claimed here by readers that do guess a size from a table of frame
dimensions. That is the same fault under a name we already own, and it is left alone rather than
quietly widened.

### The three that were implemented from their own documents

  - `oil` is the Open Image Library's own format — OpenIL's, before it was renamed DevIL. It existed
    for under a year, from December 2000 to November 2001, which is why nothing has one. Its
    specification survives in the DevIL 1.1.8 documentation as `ImageLib/docs/oil_spec/index.htm`
    and describes every field. The one thing the document does not say is whether its structures are
    packed or aligned, and the file settles that itself: the eighty-three byte description string it
    ends with can only sit at offset 22, which is where the packed layout puts it. Written that way,
    files built here in all four of the pixel types and all three of the compressions the format
    describes are read by XnView at the size and depth they were built with. Its pixels are not a
    check — handed a picture of four distinct rows it returns the last of them four times over — but
    the one row it does get right is ours to the byte. LZO is refused rather than guessed at. The
    weak point is stated on the reader: nothing in the document says which way up the rows go, and
    what says bottom-up is XnView calling the format "Bottom Left", the single row it does draw
    being the last one stored, and DevIL holding its images at a lower-left origin.
  - `tjp` is TilePic, out of the Berkeley Digital Library project: a pyramid of JPEG tiles in one
    file so a viewer can fetch the part of a large scan that is on screen. `tilepic(5)` reproduces
    the layout comment from its own source in full. What makes it safe to write without a sample is
    that the tiling is arithmetic: the layer sizes follow from the picture size and the scale, the
    tile counts follow from those, and they have to add up to the count the header states. The
    document's own worked example — 1011 by 765 in 256-pixel tiles over four layers — comes to
    eighteen tiles and the reader here agrees. Files built to it are read by XnView, which takes the
    first tile, the top of the pyramid; this takes the bottom layer, which is the picture. A file of
    a large scan therefore comes out here at the size the header states and there as a thumbnail.
  - `pdx` is Mayura Draw, formerly PageDraw, and it is not a raster format at all: the program saves
    Encapsulated PostScript under a name of its own. Handing XnView one file under both `.pdx` and
    `.eps` gets the same picture by the same route, so the name is claimed for the PostScript
    interpreter here. It costs nothing in strictness — what decides is still the two characters a
    PostScript program has to begin with, and a PNG or a JPEG arriving as `.pdx` is refused.

### `tdi`, and a reader that had never agreed with anything

`tdi` is XnView's `Explore (TDI) & Maya`, reading `iff` and `tdi` with one decoder: Explore was
TDI's renderer and Maya inherited its image format. This tree has had a Maya IFF reader for some
time. It could not read a Maya IFF.

Three things were wrong, and each of them alone would have been enough. Its header structure was
declared as 32 bytes where the format's is 24, so any file not written by this library was refused
before anything was read. It took the channel count from the name of the tile chunk, which is
`RGBA` even for a picture with three planes — the flags in the header say how many there are. And
what a tile holds between its corners and its end was recorded in the writer as unsettled, with a
note that the answer was in a real file: it took the planes in the order the tag reads forwards,
top row first, where the format names them backwards and counts rows from the bottom.

`nconvert` writing Maya IFF is the real file that note asked for. Three pictures — colour, colour
with alpha, and one flat enough that the run-length coding is used rather than skipped — go out
through it and come back through the reader here matching the originals on every pixel of all three.
Written the other way, a picture encoded here is read by XnView on every pixel as well. Two more
things fell out of doing it in both directions: the tile corners count rows from the bottom of the
picture, which a file of one tile row cannot show and a file of two shows immediately, and the
header's compression field has to name the coding even when a tile is stored uncompressed, which is
what XnView's own writer does.

`.tdi` is claimed on that. The name decides nothing — a file still has to open `FOR4`, name its form
`CIMG`, carry a `TBHD`, and have every tile's coding account for that tile's chunk exactly — and an
Amiga IFF bitmap or a JPEG under either name is refused.

### `rix`, which was already read

`rix` wants `rix sci scx sc?`, and `sc?` is a wildcard for the screen mode the file was saved in
rather than an extension. The reader here requires the file to open with `RIX3` and the row was open
because the wildcard was counted as a name.

That was left at "`.sc0` through `.sc9`, which is the whole of it", and it is not the whole of it:
`sc?` is `sc` and any one character, so the letters are in it too. All thirty-six are claimed now.
Claiming that many names costs nothing, and the reason is worth stating because it decides this row
and several like it — the extension is not what identifies a file to XnView. Its converter reads a
ColoRIX picture named `.foo`, and named `.ppt`, as a ColoRIX picture; the wildcard is what its file
chooser offers, not what its reader tests. Half the set is spoken for here by something else, and
that does not matter either: `.scr` is claimed by four formats already and a file under it still has
to open with `RIX3` before this reader takes it.

### What the rest turned out to be

Three of them are not picture formats and cannot be made into ones by a reader:

  - `wrl` is VRML 2, a language for describing a three-dimensional scene. Rendering one is a scene
    graph, a camera and a lighting model, which is a different job from reading a raster. XnView
    agrees outright: its catalogue entry for the name carries a writer and a null where the reader
    would be, so it emits VRML and has no reading of it either.
  - `pps` and `ppt` are PowerPoint. XnView's own catalogue writes them as `PowerPoint (images)`:
    what it takes out is the pictures a presentation carries, which live in an OLE compound
    document's picture stream. There is no PowerPoint picture format.
  - `tsk` is a Pocket PC theme, which is a Microsoft cabinet archive — it opens `MSCF` — holding
    the bitmaps a theme is made of. XnView's reader for it requires those four letters and then does
    nothing but scan the whole file for `GIF8`, a PNG signature or a JFIF marker and hand the *n*th
    hit to the matching decoder. It never unpacks the cabinet, so it finds only what a theme happens
    to store uncompressed. Reading the format properly means a cabinet reader, which is a different
    job; reading it XnView's way means a signature scan, which is the thing this file has twice
    refused to call a format.
  - `pzp` is an MGI PhotoSuite project, also an OLE compound document; its own stream is called
    `Catalog`.
    the bitmaps a theme is made of.

`pzp` was in that list and is out of it: an MGI PhotoSuite project is an OLE compound document, but
the pictures inside it are whole PNG files and XnView does not use the stream names to find them. It
is read now — see below.

One cannot be read at all: `pax` is Smaller Animals Software's Pick Ax format, which opens with
`PAX` and is encrypted under a password the file does not carry. Tools exist whose entire purpose is
to guess that password. Its codec is a closed Windows DLL — `pax` is the one name of the 178 that
XnView's own Linux converter does not carry at all, so there is nothing here to interrogate either.

Two are identified down to their first bytes and no further, because the coding was never published:
`pwc` is the piecewise-constant image model, `4yVa`, distributed as a Windows binary from its
author's research page; `wic` is the J Wavelet Image Codec, `FA DE BA BE 01 01` in its later form
and `1B 7A FB 30` in its earlier. Both are marked Windows-only in XnView's catalogue, so nothing
here has ever seen one decoded either.

Three have a shape worth writing down for whoever comes back to them:

  - `prc` is Sony's Picture Gear Pocket, and it is a Palm resource database — the 78-byte Palm
    header, type and creator both `IMVS`, then resources named `iINF`, `iFRI`, `iPLT` and `iTIL`.
    The picture is in the `iTIL` tiles against the `iPLT` palette. `nconvert` writes one given
    `-colours 16`, so unlike everything else in this list it can be worked on with a sample in hand
    — but not a trustworthy one. See the measurements further down.
  - `pseg` is an IBM printer page segment: MO:DCA structured fields, each introduced by `5A`. The
    architecture allows an IOCA image inside one and there is an IOCA reader here already, but that
    is not what XnView reads: its page-segment loader handles the IM1 image and nothing else. See
    further down.
  - `xp0` is the SecretPhotos puzzle, and it carries a JPEG. It is the `ecc`/`lvp`/`pan` shape — a
    wrapper round a picture format already here — and where the JPEG begins was for a long time only
    one signature scan's worth of evidence, which this file has twice been rewritten to say is not a
    format. That is settled now, from two sources that have never seen each other; the row below says
    how, and it is read.

The rest are a name and a vendor and nothing else. Searched by extension and by full name across the
Just Solve The File Format Problem wiki, TrID's definition set, Deark's and dexvert's format lists,
PRONOM, the Encyclopedia of Graphics File Formats, and general web search: `ncr` is NCR Image,
`ncy` a FlashCam frame, `pbt` Micro Dynamics MARS — a Macintosh archival system that stored scanned
documents on optical disk — `pig` a Ricoh IS30 scanner, `pixi` Pixibox, `pixp` Pixel Power Collage,
`pp4` Micrografx Picture Publisher 4 and `prisms` Prisms. That search was repeated for `skn`, `smp`,
`ssi`, `stm`, `tdim`, `tnl`, `upi` and `xim` and returned nothing for seven of the eight either — but
those eight are answered now, and not by searching. Their layouts came out of XnView's own reader,
which is in this tree and had never been read; see "Reading the reader" below. The eighth, `xim`, does
have a published description after all, and finding it was worth doing because it agrees with the
binary field for field. `pd` is the odd one: XnView calls it `Male MRI` and gives it `.pd`,
`.t1` and `.t2`, which are the names of the pulse sequences a scan is taken with rather than of a
format, so it belongs with the raw formats above — slices with nothing to say how big they are.
PRONOM, the Encyclopedia of Graphics File Formats, and general web search: `skn` is Skantek, `smp`
Xionics SMP, `ssi` SriSun, `stm` an ArcSoft PhotoStudio stamp, `tdim` Digital F/X, `tnl` a
thumbnail, `upi` Ulead PhotoImpact and `xim` Ximage.

### Asking XnView's own converter what the format is

The web has nothing to say about most of these names, and a sample of one of them turns up about
once a year. The converter has both: it reads every one of them and it is on this machine. Two ways
of asking it were used here and the second is the one that pays.

The first is interrogation: build a candidate file, hand it to `nconvert -in <name> -info`, and
correct until the width, the height and the depth come back as written. That settles a layout once
you have a hypothesis, and it settled `mfrm` in four tries. It cannot produce a hypothesis.

The second is to read the converter. Its formats live in one table in its data segment, eighty bytes
an entry — the name, the description, four slots, the loader's address, three more, the extension
list. Find the description string, find the eight bytes that point at it, and the entry's fifth slot
is the function that reads the format. Six helper functions do all the reading — a byte, a
sixteen-bit number either way round, a thirty-two-bit number either way round, and a skip — so a
loader disassembles into a list of offsets and constants that is the format's header. That is where
`ncr`'s four opening bytes, `pixi`'s twelve, `nsr`'s ten-byte header and the rest below came from.
Every one of them was then built as a file and put back through `-info`, and the pictures compared
pixel for pixel, so nothing here rests on the reading of the disassembly alone.

Two of the twenty turned out to be a format already here, and the table said so before a byte was
read: two entries pointing at one loader address are one format under two names.

  - `pd` — XnView's "Male MRI", `.pd`, `.t1` and `.t2` — has the same loader as `fre`, "Male Normal
    CT". Both are GE Genesis 5.x images, which is read here; the extensions are the Visible Human
    dataset's names for the pulse sequences, not names of formats. Claimed.
  - `ncy`, the FlashCam frame, has the same loader as `jpeg` — the one `.jps`, `.fsy` and `.mph`
    already share. It is a JPEG. Claimed.

Six more were implemented from what the loader does:

  - `mfrm`, Megalux Frame: `FRM`, a layout code that has to be four, a sixteen-bit width and height,
    then sixteen bytes that are not read, then four bytes a pixel with blue first. FFmpeg's demuxer
    puts the picture at offset eight and reads five layout codes; XnView reads one code and starts
    the picture at twenty-four, and a file built FFmpeg's way comes back shifted.
  - `pixi`, Pixibox: twelve fixed bytes, a width and a height at 14 and 16, the picture at 1024,
    run-length coded four bytes a pixel with a count of zero meaning "to the end of the row", rows
    from the bottom up.
  - `ncr`, NCR Image: `6E 6E 0A 00`, the width and the height at 0x42 and 0x46, a coding byte at
    0x4A and Group 4 coding from 0x5E. A page coded by this library's own G4 encoder and put under
    that header comes back from the converter with no pixel differing.
  - `pmp`, Sony DSC-F1: the JPEG at 124, as Klingebiel's page describes. XnView will take a JPEG
    behind a prefix of any length up to about three hundred bytes, so it would read a foreign file
    that happened to carry one; this reader requires the header to state 124 and the JPEG to start
    there.
  - `pzp`, MGI PhotoSuite: the compound-document signature, then a walk from offset 512 in steps of
    four for the eight bytes a PNG opens with, and the first one found is the picture. No stream
    name is involved — the loader does not open the directory at all.
  - `nsr`, NewsRoom: ten bytes — `00 A0`, two that are not read, a pair whose difference is the
    height, a pair whose difference plus one is the width, then `00 FF` — and the bits behind them,
    a set bit being paper. The reader that stood here checked nothing but a length of 7680 bytes and
    called every such file a 320x192 panel; the format cannot state a width of 320 at all, because
    both of its sizes are pairs of single-byte coordinates.

The rest were read far enough to say what stopped them, which is worth more to whoever comes back
than the vendor's name:

  - `mbig`, Cartes Michelin: four thirty-two-bit numbers and no signature — a tile width and height
    between 32 and 512, then a count across and down between 2 and 64. Nothing in the file says it
    is one of these, so claiming it would mean drawing any file whose first sixteen bytes fall in
    those ranges.
  - `mdl`, Half-Life Model: `IDST` and version 10, then three numbers at 0xAC that give the textures
    and their data. It is a multi-image reader over a model's skins. Implementable, but the field
    at 0xAC does not line up with the published `studiohdr_t` — where that puts the texture count is
    0xB4 — and there is no model here to settle which is right.
  - `pbt`, Micro Dynamics MARS: `02 00` and then `PBIT`, big-endian numbers behind it. The signature
    is certain and the rest was not run down.
  - `pig`, Ricoh IS30: `01 00`, a third byte choosing between two things, and then fields written as
    ASCII decimal numbers of three and four characters that it converts with `strtol`.
  - `pixp`, Pixel Power Collage: the first thirty-two bytes of the file have to equal the file's own
    name — the loader takes the basename off the path and compares — then a thirty-two-bit number at
    0x40. A reader for it would have to be given the name as well as the bytes, which nothing here
    does.
  - `prisms`: `EB E8 00 00`, the eight characters `R8G8B8A8` at 0x86, a height and a width at 0x1CC,
    and a sixteen-bit offset at 0x200 saying where the coded picture starts. The coding is a
    run-length scheme with several opcodes and was not finished. Worth noting for the row above it:
    XnView reads `lff`, "LucasFilm Format", with this same loader, so what XnView calls an LFF is a
    file opening `EB E8 00 00` and not the `LFF\0` this library's LucasFilm reader requires. One of
    the two is reading something the other does not.
  - `pseg`, IBM printer page segment: every structured field has to be introduced by `5A`, and the
    types the loader handles are `D3 A6 7B`, `D3 AC 7B` and `D3 EE 7B` — the IM1 image, not IOCA.
    An IOCA page segment built here, with and without the `5A` prefixes, is read by XnView's `ioca`
    and refused by its `pseg`, so the IOCA reader already here is not what closes this row.
  - `pp4`, Micrografx Picture Publisher 4: `II`, then a thirty-two-bit offset at 0x2A, and the
    loader copies what it finds there into a temporary file and hands it to another reader. What
    that reader is was not chased down.
  - `pps` and `ppt` share one loader and are read now; the walk is described under "Two containers
    XnView reads without opening" below.
  - `prc`, Picture Gear Pocket: the Palm resource database named above, now measured. `iINF` is
    eighteen bytes — width, height, a zero, the depth, a zero, a stride in pixels, `00 FF`, the
    record id — `iPLT` is a count and four bytes an entry, `iFRI` names the tile records, and each
    `iTIL` is a two-byte length and a 32x32 tile. It is still not implemented, and the reason is that
    there is nothing to check it against: the converter cannot read `prc` at all, not even the file
    it has just written, and the file it writes is wrong — a 16x12 picture comes back with rows 0, 2,
    4 and so on in the first half of the tile and the rest of it blank. A reader built on that would
    be agreeing with a bug.

The last column marks the ones XnView itself cannot load on this platform: its catalogue says
Windows only, so nothing here has ever been able to compare against them either.

| XnView name | extensions | |
|---|---|---|
| 2d | .2d | read by the Windows bitmap reader, which is what XnView reads it with |
| abs | .abs | read; Optocat's 16-bit header, and the extension settles it against TIFF as XnView's does |
| afx | .afx |  |
| aim | .ima | declined again; the size comes from a `.hd` beside the file, which bytes alone cannot reach |
| ami | .[b] | `[b]` is a filename prefix, not an extension — one of two bracket-or-wildcard tokens in a catalogue of 554; the files are `.ami`, read and agreeing once XnView's doubling is undone |
| anv | .anv | read; a 256-colour DIB with AN for BM, at fixed offsets |
| aphp | .php | every photograph read, not the theme artwork |
| apx | .apx | read; MXPaint's two signatures, a layer table, and ABGR rows from the bottom up |
| arf | .arf | read; XnView's ARF is not Axon's, which it refuses |
| arn | .arn | read; the label, then three colour tables at their own padding, then the rows |
| aurora | .sim | read; it is a Pictor page and the reader of those was wrong |
| avs | .mbfavs .mbfs .x | read |
| b3d | .b3d | read |
| bfli | .flp | read at 320x400 now, the two FLI frames de-interleaved, and `.flp` claimed on it |
| bias | .flt .msk |  |
| bif | .bif | read; FA BA, 374 bytes nothing reads, then one whole JPEG |
| bmc | .bmc | read by the Windows bitmap reader; the embroidery format of that name is stitches |
| bmf | .bmf | Windows only |
| bmg | .bmg .ibg |  |
| bms | .bms | read; BMSWinPlay and a Windows bitmap |
| bpr | .bpr | read; XnView's reader for the name is its GIF reader, function for function |
| btn | .btn |  |
| bum | .bum |  |
| cam | .cam | read, both camera generations |
| car | .car | read; SN, an offset, and a PNG at it |
| cat | .cat |  |
| cbmf | .bmf |  |
| cdr | .cdr |  |
| cft | .ctf | read; XnView's reader for the name is its TIFF reader, function for function |
| cgm | .cgm | binary encoding read; character and clear-text refused |
| cloe | .cloe | read |
| cmt | .cmt | read; the demosaic in double, which is where XnView and cmttoppm.c part company |
| cmx | .cmx |  |
| cncd | .ncd |  |
| crd | .crd | read; a length-prefixed CardMaker, then the JPEG found by its own JFIF identifier |
| crw | .crw |  |
| cvp | .cvp | read; 512x512 in three planes and nothing else — the length is the whole signature |
| d3d | .b2d .b3d |  |
| dsi | .dsi | read; `DI`, four fields at fixed places, Group 4 or uncompressed |
| dwg | .dwg | thumbnail at the stated address |
| dxf | .dxf | read from the published group codes; Windows only in XnView |
| ecc | .ecc |  |
| eidi | .ei .eidi | read; eighteen samples, every pixel agreeing with XnView |
| eif | .eif | read; the document's pages are whole TIFF streams |
| eri | .eri | Windows only |
| fbm | .cbm |  |
| fff | .fff | read; its signature stands at 452 and its JPEG at 3272 |
| fi | .fi | read; a zlib stream holding the palette and then the rows, or a JPEG at 598 |
| fif | .fif | Windows only |
| fre | .fre | read; a GE Genesis image, met as the Visible Human CT slices, and the Male MRI slices too |
| frm | .frm |  |
| frm2 | .frm |  |
| fre | .fre | read; a GE Genesis image, met as the Visible Human CT slices |
| frm | .frm | read; XnView's reader for the name is its JPEG reader, function for function |
| frm2 | .frm | read; XnView's reader for the name is its PNG reader, function for function |
| fsy | .fsy |  |
| fx3 | .fx3 | read by the TIFF reader, which is what XnView reads it with |
| gem | .gem | read |
| gm | .gm .gm2 .gm4 | read; FF04 0007, a level byte at 17, and the ICL manual's byte pair |
| hdri | .hdri |  |
| hdru | .gn .hdru | read uncompressed; the Group 3 and Group 4 cases are refused |
| hpgl | .hgl .hpg .hpgl .prn .prt | read |
| hru | .hru |  |
| hta | .hta | read; a directory of PNGs, written so that deark and XnView both take it |
| icd | .idc | read; the header is a 32-byte trailer and it ends in IDC21 |
| icon | .pr |  |
| iff | .blk | read |
| iimg | .iimg |  |
| imi | .imi | read; TMSat, headerless, at the one length the format has |
| imt | .imt | read; 27 43 31 00, a 22-byte header, Group 4 |
| ioca | .mod |  |
| ipg | .ipg |  |
| iss | .iss | read; 3KCBIMSP, one or eight bits a pixel, counting upward from white |
| iwc | .iwc | header recovered from WaveL's own applet; the subband coding is not. Windows only |
| jbf | .jbf | version 2 read; version 1's bitmap coding refused |
| jig | .jig |  |
| jig2 | .jig |  |
| k25 | .k25 |  |
| kps | .kps |  |
| kqp | .kqp |  |
| kskn | .thb | read by the Windows bitmap reader, which is what XnView reads it with |
| lda | .lda | read; DC DC, a 512-byte header, Group 3, Group 4 or uncompressed |
| ldf | .ldf | LuraTech, binary-only like LuraWave. Windows only |
| lvp | .lvp |  |
| lwf | .lwf | Windows only |
| lwi | .lwi |  |
| mbig | .big | a tile grid with no magic at all: four numbers, a tile size between 32 and 512 and a count between 2 and 64 |
| mdl | .mdl | declined: IDST and version 10, and the field XnView reads at 0xAC is not where studiohdr_t puts the texture count |
| mfrm | .frm | read; FRM, the one layout code XnView takes, and the picture at 24 rather than at 8 |
| mix | .mix |  |
| mjpg | .wi |  |
| mph | .mph |  |
| mrf | .mrf |  |
| mrw | .mrw |  |
| mtx | .mtx | read; the constant, the size, the width of a pixel, and the pixels |
| ncr | .ncr | read; 6E 6E 0A 00, the size at 0x42 and 0x46, and Group 4 coding from 0x5E |
| ncy | .ncy | read by the JPEG reader, which is the loader XnView reads it with |
| nsr | .bn .ph | read; a ten-byte header, and the panel it states cannot be the 320x192 the old reader assumed |
| oil | .oil |  |
| mtx | .mtx |  |
| ncr | .ncr | read; 6E 6E 0A 00, the size at 0x42 and 0x46, and Group 4 coding from 0x5E |
| ncy | .ncy | read by the JPEG reader, which is the loader XnView reads it with |
| nsr | .bn .ph | read; a ten-byte header, and the panel it states cannot be the 320x192 the old reader assumed |
| oil | .oil | read, from the Open Image Library's own specification; no sample was available |
| pan | .pan |  |
| pax | .pax | Pick Ax, Blowfish under a password the file does not carry; XnView's Linux converter does not carry it either |
| pbt | .pbt | Micro Dynamics MARS: 02 00 and then PBIT; the fields behind it were not run down |
| pcl | .pcl | the raster subset read; text and HP-GL/2 passed over |
| pd | .pd .t1 .t2 | read by the GE Genesis reader, which is the loader XnView reads it with |
| pdd | .pdd |  |
| pdx | .pdx | read; Mayura Draw saves Encapsulated PostScript under a name of its own |
| pegs | .pxa .pxs |  |
| pig | .pig | Ricoh IS30: 01 00, a mode byte, then fields written as ASCII decimal numbers |
| pixi | .pxb | read; twelve fixed bytes, the size at 14, and a run-length picture at 1024 from the bottom up |
| pixp | .i17 .i18 .ib7 .if9 | Pixel Power Collage: the first 32 bytes have to equal the file's own name, which a reader of bytes cannot check |
| pmp | .pmp | read; the JPEG at 124, and the size the header states is not the picture's |
| pmsk | .msk | read by the Windows bitmap reader, which is what XnView reads it with — the Paint Shop Pro reader that used to hold this name alone could not have read one |
| pp4 | .pp4 | Micrografx Picture Publisher 4: II, an offset at 0x2A, and XnView hands what is there to another reader |
| pp5 | .pp5 |  |
| pps | .pps | read; one reader with `ppt`, walking the OfficeArt records from offset 512 for the first JPEG or PNG BLIP |
| ppt | .ppt | read; one reader with `pps` |
| prc | .prc | Picture Gear Pocket, measured but not built: the converter cannot read one, and the one it writes is wrong |
| prf | .prf |  |
| prisms | .pri | Prisms: EB E8 00 00 and R8G8B8A8 at 0x86; the run-length coding was not finished |
| pseg | .pse | IBM page segment: the 5A-introduced fields XnView reads are the IM1 image, not IOCA |
| pspb | .pspbrush |  |
| pspf | .pfr .pspframe | read by the Paint Shop Pro reader |
| pspm | .pspmask |  |
| pspt | .tex | read by the Paint Shop Pro reader |
| pwc | .pwc | the piecewise-constant image model; its coding was never published; Windows only |
| pxa | .pxa |  |
| pzl | .pzl |  |
| pzp | .pzp | read; a compound document, walked from 512 for the first whole PNG in it |
| qcad | .cad |  |
| raw | .grey .gry | raw greyscale; XnView asks the operator for the size and its own reader requires it |
| rfax | .001 | Ricoh Fax; signature and header recovered, the page coding not |
| rix | .sc? | `sc?` is a wildcard, not an extension; it stands for `sc` and any one character and the ColoRIX reader claims all thirty-six now |
| sct | .ch | read by the Scitex CT reader |
| sdg | .sdg |  |
| sfax | .001 | SmartFax; signature and header recovered, the page coding not |
| sid | .sid | Windows only |
| skf | .skf |  |
| skn | .skn | Skantek; the 740-byte header recovered, the CCITT coding not |
| smp | .smp | Xionics SMP; signature recovered, the tagged header not mapped |
| ssi | .ssi | read; SriSun, recovered from XnView's own reader and checked against it |
| ssp | .ssp | every embedded picture read, not the first |
| stm | .stm | read by the Windows bitmap reader, which is what XnView reads it with |
| stw | .stw |  |
| svg | .svg | read |
| synu | .syn .synu |  |
| taac | .suniff .taac .vff | read, and checked against the sample |
| tdi | .tdi | read by the Maya IFF reader, which had to be corrected first |
| tdim | .tdim | read; Digital F/X, recovered from XnView's own reader and checked against it |
| ti | .73i .82i .83i .85i .86i .92i |  |
| tile | .tile |  |
| tjp | .tjp | read, from tilepic(5); the bottom layer rather than the first tile |
| tnl | .tnl | read; DISPTNL, a grey or a JPEG at 168 |
| tsk | .tsk | read; a Microsoft cabinet scavenged for a GIF, PNG or JFIF stored in it whole, which is all XnView does with one |
| ttf | .ttf | drawn as a sheet of its glyphs |
| tub | .psptube .tub |  |
| upe4 | .pe4 |  |
| upi | .upi | read by the Windows bitmap reader, which is what XnView reads it with |
| upst | .pst |  |
| uyvy | .qtl | raw YUV; nothing states the size, and five names share this extension |
| uyvyi | .qtl | raw YUV; nothing states the size, and five names share this extension |
| vit | .vit |  |
| vob | .vob |  |
| wic | .wic | the J Wavelet Image Codec; its coding was never published; Windows only |
| wrl | .wrl | VRML 2; the catalogue row has a null where the reader's address goes, and the converter refuses the `.wrl` it has just written |
| wzl | .wzl |  |
| x3f | .x3f |  |
| xar | .xar | preview at the stated tag |
| xif | .xif | read by the TIFF reader, which is what XnView reads it with |
| xim | .xim | read; Thompson's Xim, eight-bit planes; netpbm's header and XnView's reader agree |
| xp0 | .xp0 | read; 00 00 00 01 and a JPEG at 1779, which two sources agree on |
| ypc | .ypc | Windows only |
| yuv411 | .qtl | raw YUV; nothing states the size, and five names share this extension |
| yuv422 | .qtl | raw YUV; nothing states the size, and five names share this extension |
| yuv444 | .qtl | raw YUV; nothing states the size, and five names share this extension |
| zbr | .zbr |  |
| zmf | .zmf |  |

### Reading the reader

`nconvert` does not only write six of these formats and read a file built to a guessed layout. It
*contains* a reader for nearly every name in this list, and that reader is a description of the
format — the only one that exists for most of them. Its catalogue is an array in the binary of
eighty-byte entries: the short name, the description, the list of extensions, and at offset 0x20 the
address of the function that reads it. Five hundred and sixty of them.

Two things fall out of that before a single byte is decoded. The entry for `wrl` has a null there,
which settles VRML outright: XnView writes it and cannot read it, so there is nothing to match. And
several names share one function, which settles three more the way `2d` and `bmc` were settled.
`stm` and `upi` — PhotoStudio Stamp and Ulead PhotoImpact — point at the same function as `bmp`,
`dib` and `2bp`, and `xif` points at the same one as `tiff`, `adt` and fifteen other fax names. Each
was confirmed the way the earlier three were, by renaming a file of the underlying format to it and
watching XnView report the right size, and by renaming a foreign one and watching it refuse: a JPEG
under `.stm`, `.upi` or `.xif` is refused, and so is a Windows bitmap under `.xif`.

`xif` is worth a sentence more, because its row said the sample's compression 34673 "is not
decoded" and left open whether that was documented anywhere. It is. Xerox's own XIFF 3.0
specification survives, and it names the private compressions outright: 34667 token-based, 34668
wavelet, 34672 lossy dither, and 34673 the same coding as 34667 without loss. So it is describable,
and it is a whole coding rather than a variant — which is why the name is claimed for the TIFF reader
and files using Xerox's own compressions are still refused rather than shown as a blank page.
(Two neighbouring tag numbers had been suspected of being Xerox's as well; they are not. 34675 is
the ICC profile tag, and Xerox's private *tags* are 34730 and 34732.)

For the rest, the function says what the header is. Four helpers do all the reading in it and each is
four instructions long, so which of them a field goes through says its width and its byte order
outright. That is a specification, and it was checked the same way a specification would be: a file
was built to it, handed to `nconvert -info`, and then converted to a PNM and compared to what was
encoded. Every layout below is one XnView reads at the size and depth it was built with, with the
pixels it hands back equal to the ones written.

  - `ssi` is SriSun: `srisunim`, a byte that has to be zero for the picture to be readable at all,
    the depth, a byte that has to be 2, then the width and the height as big-endian words, all inside
    a 256-byte header with the rows after it uncompressed. One, four, eight, sixteen and twenty-four
    bits. There is no colour table anywhere in the file and the reader never looks for one, so the
    shallow depths are greys — a set bit is white — and sixteen is five bits a channel in a
    little-endian word, widened by the exact fraction rather than by repeating the high bits, which
    is what made 0x2011 come back as 65, 0, 139 rather than 66, 0, 140.
  - `xim` is Ximage, and it is the one name where a published description turned up to check the
    binary against. It is Philip Thompson's Xim, out of the X11R4 contributions, and netpbm still
    carries its header file: eleven decimal numbers in fixed-width ASCII fields, four free-text
    fields, and a 256-entry colour table, coming to the 1024 bytes the second field states. Every
    offset recovered from XnView's reader is the offset netpbm's `xim.h` gives, which is two
    independent readings of the same document agreeing. The planes are whole — all the rows of the
    first, then all the rows of the second — and each row is either flat or coded as a count one less
    than the run and the byte to repeat. Only the eight-bit planes are read: the header can also say
    one bit, and where it says the picture has an alpha channel XnView takes a different path through
    the body that nothing here has seen a file for. Both are refused rather than read as though the
    field said something else.
  - `tdim` is Digital F/X: `00 02 00 20`, four bytes nothing reads, the height and the width as
    big-endian words in that order, and a big-endian long saying where the picture begins. Four bytes
    a pixel, run-length coded, and the first of the four is not drawn — which was settled by giving
    all four channels different values and reading back which three came out.
  - `tnl` is Thumbnail: `DISPTNL` and then one byte that decides which of two files it is. `5` means
    an ordinary JPEG at 168; anything else means the file states its own size as two little-endian
    longs at 16 and 20 and the picture is one byte a pixel from 168.
  - `xp0` is the SecretPhotos puzzle, and this is the row that says why reading the reader is worth
    more than reading a sample. This file has twice been rewritten to say that the JPEG's offset was
    one signature scan's worth of evidence and so could not be claimed. XnView's reader requires four
    bytes reading `00 00 00 01` and then seeks to 1779 — a constant in the code, not an accident of a
    sample. TrID's definition, built from seven files nobody here has, records `JFIF` at 1785, and a
    JFIF's `JFIF` stands six bytes past the start of the picture. Two sources that have never seen
    each other put the picture in the same place. It is claimed, with the requirement that a JPEG
    actually opens there.

### The .qtl family, and the fault it exposed, which is now run down

Five of the remaining names — `uyvy`, `uyvyi`, `yuv411`, `yuv422`, `yuv444` — all sit on `.qtl`, and
`raw` sits on `.grey`/`.gry` the same way. None of these states its size, five mutually incompatible
layouts share the one extension, and `.qtl` is separately registered as QuickTime Media Link, which
is an XML playlist. Those rows stay open and the reason has not changed.

What has changed is that the readers behind them can now be read. The two that were read — `uyvy` and
`uyvyi` — open by taking the width and the height from the command line and returning an error where
either is absent, which is what its help says of all of them and what its behaviour shows: it refuses
every one of these files on this platform whether the size is given or not. Two facts came out of
those two that are worth keeping.

The first is that `uyvyi` carries a table of twenty-five frame sizes and places a headerless stream by
matching its length against them — exactly the rule the reader here uses, against a list that had
been guessed. That list is now XnView's, in XnView's order, which matters: 720 by 512 and 640 by 576
are the same number of bytes, and taking them in that order is what makes this reader place such a
stream where that one places it. Five sizes this reader already accepted and that list does not name
are kept after them, so they can only ever settle a length XnView refuses outright.

The second is the fault recorded here last time, which was two faults.

One is the range. A frame written by that converter and read as though the samples filled the whole
byte returns pure red as 237, 15, 14; the stream is studio swing, luma 16 to 235 and chroma 16 to
240. Read as studio swing the same frame returns 253, 0, 0, and over a chart of saturated colour bars
the mean error falls from 15.2 of 255 to 0.26, worst 3.1 — which is what the halved chroma costs at a
bar edge and nothing else. That correction is in.

The other is why correcting the range appeared not to help. XnView has two names for this one stream
and the difference is not in the pixels: its `uyvyi`, "YUV 16Bits Interleaved", stores the rows in
order, and its `uyvy`, "YUV 16Bits", stores the even rows of a frame and then the odd rows. Read one
as the other and the picture comes back correct at the first row and the last with the top field's
colours through the middle — the "red in the middle" recorded before — wrong by 61 of 255 on average.
Nothing in a headerless stream says which it is, and both names claim `.uyvy`, so the progressive
reading is the one taken, that being what the four letters mean everywhere they name a capture buffer.

There was a third thing, and it is worth recording because it nearly went in as a finding. Fitting
the colour matrix over 256 random colours said the converter's chroma was exactly three quarters of
the standard's. It is not. The test picture was two pixels wide, and the chroma filter that halves
the horizontal resolution runs off both ends of so short a row and pulls the difference towards
neutral. At 720 pixels the same colours come out at exactly the standard's numbers — red as 91, 82,
240 — and the three quarters was a measurement of the test rather than of the format.

`raw`, on `.grey` and `.gry`, is the same shape and stays open for the same reason: `-size` is where
its size comes from. The reader here under `.raw` guesses one from a table, which is the same fault
under a name we already own; it is left alone rather than quietly widened.

### The four whose headers are now known and whose pixels are not

Reading the reader gave these four a signature and a header and stopped there, because what follows
the header is a coded bitstream that would have to be implemented rather than described. They are
recorded so the next attempt starts here rather than at the name.

  - `skn` is Skantek. Four big-endian longs — `FFFF0001`, `FFFFFFFE`, `FFFD0000`, `00000000` — then
    286 bytes skipped, the six characters `920101` at 302, 424 more bytes skipped, and the height and
    the width as big-endian longs at 732 and 736. The header is exactly 740 bytes. What follows is
    one bit a pixel through XnView's CCITT decoder; there is a CCITT decoder here already, so the
    remaining work is which of its codings the format uses.
  - `smp` is Xionics SMP: a zero word, `Xionics `, then `F`, `1B`, `7F`, `00`. After that the header
    is a run of tagged fields with fixed constants between them — `1B`, `19`, `02`, `1A`, `02` in
    that order — which were not mapped to their meanings.
  - `rfax` is Ricoh Fax: two bytes, then the fourteen characters `FAXNET / RICOH`. The pages begin
    at 256 and there can be up to 4300 of them, each a fixed-size record fed to a strip decoder.
  - `sfax` is SmartFax: the five characters `FAX1D`, a word, two bytes, then a byte that is only ever
    tested for zero — it selects 100 or 200 dots to the inch — and five more bytes.

This also settles something about two readers already here. `RicohFaxFile` requires `RICF` and
`SmartFaxFile` requires `SMFX`, each in front of a header of its own invention. Neither signature
appears in the format XnView reads under that name. They are readers that agree with nothing but
themselves — the twelfth and thirteenth found here — and they are left standing only because
replacing them means implementing the real coding, which is the work above.
Correcting the range to studio swing on both sides brings that to 42 and no further, and what is left
is not a range fault: a vertical red-to-blue gradient comes back correct at the top row and the
bottom row and shows red in the middle. Both ends right and the middle wrong is a structure fault,
not a coefficient one, and it was not run down — the correction is reverted rather than half-shipped.
So the name stays open and the fault is recorded — it is in a reader we already ship
under a name we already claim, which is the more useful half of the finding.

### The converter's own format table, and twenty-two names off the back of it

The interrogation above treats the converter as a black box: build a file, ask what it made of it,
correct, repeat. It is not a black box. `nconvert` is a stripped but unpacked ELF whose `.rodata` and
`.text` sit at their file offsets, and it carries **a table of 567 entries, eighty bytes each**, in
`.data.rel.ro`. Each entry is the format's short id, its display name, the address of the function
that reads it, and its extensions. Every name in this file can be found in it in one grep, and with
it the exact function to read.

That turns the loop from guess-and-check into read-then-check, and it answers one question a black
box cannot: **whether two of XnView's names are one reader**. Four of the names here are.

  - `bpr`, "AAA logo", is its **GIF** reader — the same function address as `gif`.
  - `cft`, "Optigraphics", is its **TIFF** reader, as is its neighbouring "Optigraphics Tiled".
  - `frm`, "PhotoFrame", is its **JPEG** reader.
  - `frm2`, "Album", is its **PNG** reader — and `.frm` is therefore claimed twice over. Of the
    twenty-four `.frm` files in this corpus exactly one is a picture, and it is a JPEG; the other
    twenty-three are documents, and both readers here refuse all of them, as XnView refuses them.

Sharing the function means the behaviour is not similar but identical, and renaming a file of each
format confirmed it: the converter reports the picture and names the reader `bpr`, `cft`, `frm` and
`frm2`. All four are claimed for the readers already here, on the usual test — a JPEG under `.bpr`
or `.ctf`, and a GIF under `.frm`, are refused by every reader that claims those names.

The other eighteen were recovered from the disassembly and then confirmed the way everything else
here is: a file built byte by byte, `-info` reporting the size and depth that went in, and `-out pnm`
giving back the pixels that went in. **Every one of the eighteen is byte-identical on pixels**, and
where a field could not be explained it was flipped one byte at a time until it could.

  - `ami`, Amica Paint, was already read here and is left where it was, with one thing measured that
    had not been: on the three files in this corpus every pixel this library draws matches both of
    XnView's, which draws the same picture at 320 across by doubling each multicolour pixel where
    this library returns the 160 stored ones.
  - `cvp`, "Portrait", has no header at all: the file must be exactly 786,432 bytes, which is
    512 by 512 in three planes, red then green then blue. The length is the whole signature, so the
    reader is reached by extension and never claims a file by content.
  - `abs`, Optocat, is the 16-bit header recovered in the pass before this one, and the objection to
    it — that `II`/`MM` is TIFF's opening too — was settled rather than argued. A file was built that
    is a valid TIFF *and* a valid Optocat picture; the converter reads it as TIFF under `.tif` and as
    Optocat under `.abs`, so the extension decides, and that is what this reader does too. It also
    carries the lowest detection priority and a signature test that only says yes when the offset is
    past 2047, the samples are one to four and the raster fits, so content sniffing never prefers it.
  - `icd`, Core IDC, keeps its header as a **32-byte trailer** ending in `IDC21`, with the picture at
    byte 0 — which is why looking for a signature at the front found nothing. Three planes are stored
    whole, one after another, not interleaved.
  - `iss` opens `3KCBIMSP` and counts **upward from white**: an eight-bit sample of 0x28 comes back
    as 0xD7, and a set bit in the one-bit kind is black.
  - `arn` is the PDS-style label the pass before this one described, with one correction: the picture
    does not begin at RECORD_BYTES times LABEL_RECORDS. Behind the label stand a 1024-byte gap and
    three 256-byte colour tables, each padded up to the record size, and only then the rows. Reading
    it as the earlier note said would have drawn the palette as the top of the picture.
  - `lda` (LaserData), `imt` (IMNET) and `dsi` (CImage) are the three document rasters the vendors
    describe as Group 3 and Group 4 and nobody describes further. All three are now read, on the
    decoders already here rather than a third copy of them, and the CCITT payloads the tests use are
    the converter's own encoder output rather than ours — so the decoders are measured against a
    foreign encoder. LaserData's Group 3 wants a T.4 end-of-line in front of every row; without them
    the converter draws a blank page, which is how that was found.
  - `bif` (byLight) and `crd` (PowerCard maker) and `fff` (MAGGI Hairstyles & Cosmetics) are three
    more of the carriers this file keeps meeting. byLight is `FA BA`, 374 bytes its own reader never
    looks at, and one whole JPEG — pinned by moving the payload one byte either way, which stops the
    file being taken. PowerCard is a length-prefixed `CardMaker` and then a JPEG located by its own
    JFIF identifier rather than by a fixed offset. MAGGI's signature is the lowercase
    `hairstyles & cosmetic` with two trailing spaces at offset 452, and its JPEG at 3272; one space
    or three, or the picture one byte out, and the file is refused.
  - `hta`, Hemera Thumbs, was the one case where two implementations disagreed, and both were right
    about something. XnView checks only the first four bytes of the magic and requires the first
    member to stand at 64 or beyond; deark checks all eight and does not. The file written here
    satisfies both, and deark extracts from it the same bytes the directory points at.
  - `apx` is not a carrier but a real raster: two signatures, `MXPaint-NickAvrionov` and
    `MXPaintPro-NickAvrion`, a layer table, and four bytes a pixel in the order alpha, blue, green,
    red, stored bottom row first.
  - `fi`, Flash Image, hides nothing exotic: the codec behind it is **stock zlib**, and the 0x70-byte
    context passed around is a `z_stream`. The stream holds the palette first and then the rows. Its
    modes 1 and 2 are a different thing entirely — a JPEG at offset 598, whose own header gives the
    size. `SURPRISE.FI`, the only `.fi` in this corpus and one the converter refuses, is **not this
    format**: it opens `FTC\0` and is an Iterated Systems fractal image sharing the extension.
  - `gm`, Autologic, is `FF04 0007` — TrID's signature from twelve real files — then the width, the
    height and a **level byte at offset 17** that gives both the depth and the coding. 255 means raw
    eight-bit samples; anything else means the byte pair the Input Command Language manual
    documents, where a byte with the top bit set repeats the sample before it. Sixty-seven files
    covering every level from 0 to 255, both codings and widths from 1 to 640 all come back
    byte-identical, and the four malformed ones the converter refuses are refused here too.

### Two disagreements worth keeping rather than papering over

`cmt`, the Chinon ES-1000, is the one place where the public reference implementation and XnView are
both available and do not agree. YOSHIDA Hideki's `cmttoppm.c` works the interpolation and the
saturation in `float`; XnView works all of it in `double`, and carries the square root of the
saturation as the full `1.224744871391589` where the C rounds it to a float first. Over fifteen files
built from known CCD values and run through both, they agree on thirteen and differ on two, by 15
samples of 361,500 and by 37. In a step that divides three times by a neighbour's own estimate, a
last-bit difference grows into a whole level. XnView is the standard here, so the port is in double,
and all fifteen are byte-identical to it — including the two where `cmttoppm.c` is not.

`bfli` is now right and still not byte-identical, and the difference is entirely the palette. The
reader drew 320 by 200 in high resolution; the format is 320 by 400 in two interleaved multicolour
FLI frames, which is what RECOIL and XnView both report, and what it draws now. The file is exactly
33,795 bytes: `FF 3B` for the load address, `'b'`, and 33,792 bytes that fill one buffer in an order
that accounts for the file to the byte. Against the converter on all three samples here **every one
of the 128,000 colour indices agrees**, and substituting XnView's sixteen idealised colours for
Pepto's measured ones makes all three byte-identical. They were not substituted: that table is shared
by every C64 format here and the standing decision is that RECOIL is the reference where two readings
are both defensible. `.flp` is claimed on the corrected reader, which now requires the exact length,
the load address and the marker; `.fli` and `.afl` from the same catalogue row were left alone,
because Autodesk FLIC and AFLI own them here.

### `aim`, declined a third time, and a reader that had never read anything

`aim` is the one name of the twenty-three left open, and the disassembly settles why rather than
guessing at it. XnView takes the picture's path, strips everything after the last dot, appends the
four bytes `.hd` — the string is at rodata 0x37b2e0 — and opens that sidecar: `AA` at offset 4, the
width at 22, the height at 24. Failing to find it, it accepts a file of exactly 65,536 bytes as
256 by 256. The first cannot be reached from an interface that is handed bytes, and the second is
the `ioca` objection in another form: every 64K file under the name would be drawn.

What is new is that the reader this library shipped for the name is gone. `AimGrayScale` was built
on a four-byte `AIM\0` signature that exists nowhere — not in XnView's reader, not in any
specification, not in any file — so it read nothing but what it had itself written, and it claimed
`.aim`, which is not even an extension XnView's `aim` name holds. It is the twelfth reader of that
kind found here, and it is removed rather than left with a note against it. A reader that agrees
with nothing but itself is worth less than no reader.

### The .qtl family, closed on the second attempt

Five rows — `uyvy`, `uyvyi`, `yuv411`, `yuv422`, `yuv444` — share `.qtl` and state no size, and the
first attempt at them was reverted because the decode was wrong twice over: the samples were read as
full-byte range where they are studio swing, and the two orderings were confused, which showed as a
gradient correct at its first and last rows and wrong through the middle. Both are settled, and a
frame written by XnView's own converter now comes back within a quarter of a level.

The length still has to be exactly one of the frame sizes the layout is made in, and anything else is
refused — a picture named `.qtl` that is really a PNG is turned away by name.

Worth recording that the converter will not read that frame back at all on this platform. It takes
the size from its command line and refuses a file carrying only pixels, so these five are closed by
reading what its catalogue lists and the tool itself declines to.

### Two containers XnView reads without opening

`pps`, `ppt` and `tsk` are three rows and two readers, and neither reader unpacks the container it
is given. Both were read out of the converter's own code and then checked by building files and
asking it what they are.

A presentation is a Microsoft compound document. XnView's reader for it never opens the directory
and never looks for the Pictures stream by name: it checks the signature, steps to offset 512, and
walks eight-byte OfficeArt record headers, stepping over every record by the length it states. Two
record types stop the walk — `0xF01D` at instance `0x46A`, a JPEG stored in RGB, and `0xF01E` at
instance `0x6E0`, a PNG — and the picture begins seventeen bytes into the record's data, behind one
checksum and a tag byte.

The instance is part of the test and not decoration, which is the kind of thing only the code says.
Ten fixtures were built and put to the converter: it read the PNG BLIP, the JPEG BLIP, and a file
with a filler record in front of the picture; it refused the same PNG at instance `0x6E1`, the same
JPEG at instance `0x6E2`, a bare PNG standing at offset 512 with no record around it, and a BLIP
inside an `0xF001` container. The reader here agrees with all ten, and the pixels it returns are
byte-identical to what the converter writes out for the five it reads.

That last refusal is what separates this from `pzp`. A PhotoSuite project is closed by searching for
a signature; a presentation is not, because the walk steps over containers whole and never sees what
is inside one.

A Pocket PC theme is a Microsoft cabinet, and the reader for it does not unpack that either. It
checks `MSCF` and scans the remaining bytes for `GIF8`, PNG's `89 P N G`, or a JFIF's `FF D8 FF E0`,
decoding from the first hit — which finds only what the cabinet happened to store uncompressed. The
JPEG test is on four bytes and not three: a file whose JPEG opens `FF D8 FF E1` is refused under this
name by the converter, which then falls through to its own general JPEG scan and reports it as
something else. Seven fixtures, seven agreements, and the pixels match on all four it reads.

So `tsk` is at parity rather than complete, and the distinction is worth keeping: a theme whose
pictures are all MSZIP or LZX packed is invisible to XnView and is invisible here.

### The two rows whose extension was not an extension

`ami` and `rix` are the only two entries in a catalogue of 554 whose extension list carries a bracket
or a wildcard. `[b]` is the prefix a Commodore file carries in front of its name, and `sc?` is `sc`
and any one character. The list these rows came from read both as names to claim, which is why they
were open at all. ColoRIX is dealt with above.

Amica Paint is `.ami` and is read, and the doubling recorded against it was measured again rather
than inherited: a screen written here is reported by the converter as 320 by 200 where this reads it
as 160 by 200, and all 32,000 pixels agree once each of ours is matched against the pair XnView makes
of it.

`wrl` is the third of that kind and settles by construction. Its row in the converter's format table
has a null where the address of the reader goes — one of six such rows, and the other five are
`bmp565`, `guetzli`, `jpegli`, `pcl` and `csv`, every one of them something XnView writes and does
not read. The converter writes a `.wrl` happily, as a VRML2 `PixelTexture` node, and then refuses to
read the file it has just written. Nothing here claims the name either. The row is a disposition, not
a gap: the catalogue lists a format its own reader does not have.

### The refusal audit, and the two things it found

Eighteen extensions were closed this week by claiming them for a reader already here, on the test
that the reader would refuse a foreign file arriving under the name. That test had been run with
noise and a PostScript program. Running it again with the three things such a file is most likely to
really be — a JPEG, a PNG and a Windows bitmap, each renamed to the claimed extension, fifty-four
files in all — found two defects.

`.prn` and `.prt` are HP-GL's, and the reader drew a PNG under them as a picture three pixels square.
The cause is that the parse read the whole file as text and asked only for one instruction that moves
the pen and states where to; eight kilobytes of compressed bytes carry that by accident sooner or
later. HP-GL is printable ASCII and nothing else between its instructions — the bytes outside that
set which a plot may legitimately carry all sit inside a label, a comment, or the Polyline Encoded
alphabet, and each of those is consumed whole by the parse. Requiring it refuses all three, and the
PostScript samples are still refused for the reason they were before.

`.msk` was claimed for the Paint Shop Pro reader because XnView titles the entry PaintShopPro Mask.
The title is not the reader: that entry runs the same code as `.bmp`, one reader shared by twelve
names, and Paint Shop Pro's own mask has a separate entry of its own under `.pspmask`. So the reader
that held the name would have refused every file the name was claimed for — a row closed on paper
and open in fact. The Windows bitmap reader holds it now as well, and a real `.msk` is read.

Nothing else in the fifty-four was drawn by anything it should not have been. The three acceptances
that remain are the ones that ought to be there: a Windows bitmap under `.msk`, `.stm` and `.upi`,
and a JPEG under `.ncy`, which are the formats those names are.

Two further things fell out of reading the table rather than the names, and are recorded here rather
than acted on. `.pspmask`, `.psptube`, `.pspbrush`, `.pspframe` and `.pat` are Paint Shop Pro's own
resource names on its own reader and nothing here claims any of them. And the catalogue on this
platform has no `hpgl` row at all — `prn` there belongs to PostScript; HP-GL is a Windows-only
third-party plugin, and `.prn` and `.prt` come from the plugin's line in `Formats.txt`, which is
where the gap list was generated from.
