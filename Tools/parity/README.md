# Parity report

Answers one question: is there anything RECOIL, XnView or IrfanView reads that we cannot read, or
read differently?

That is the question replacing them turns on. Counting how often we agree with a tool does not
answer it — agreeing on nine formats out of ten is no comfort if the tenth is one somebody has.

## Two tools agreeing is not always two opinions

The rule worth applying to a disagreement is that one tool differing from us is an opinion and two
agreeing against us is a defect. It holds for the machine formats, where each tool wrote its own
reader, and it fails for the mainstream ones, where they all call the same library.

The case that showed it: two JPEGs state a restart interval and carry no restart markers. XnView,
ImageMagick and IrfanView all render the first eight rows and fill the remaining 340 with flat grey.
That is not three tools agreeing on a picture — it is libjpeg giving up at the first missing marker
and concealing the rest, seen three times. Both files decode in full here, and the disagreement was
the evidence of it.

So before treating agreement as consensus, ask whether the tools could be sharing a decoder. Expect
they are for JPEG, PNG, TIFF, WebP, AVIF, HEIC and JPEG 2000, and expect they are not for anything
that came off an eight-bit machine.

## Running it

Set whichever tools are installed; the rest are skipped and say so.

```sh
export RECOIL2PNG=/path/to/recoil2png
export NCONVERT=/path/to/nconvert
export IRFANVIEW='C:\iview\i_view64.exe'
export WINEPREFIX=/path/to/prefix

samples=/path/to/samples
out=/tmp/parity

dotnet run --project Tools/parity/Decode -- "$samples" "$out/ours"   # our own decode
Tools/parity/decode-with.sh recoil    "$samples" "$out/recoil"
Tools/parity/decode-with.sh xnview    "$samples" "$out/xnview"
Tools/parity/decode-with.sh irfanview "$samples" "$out/irfanview"

python3 Tools/parity/compare.py "$out"
```

IrfanView is much the slowest — it runs under Wine and pays a process start per file.

## Reading the result

`only we read it` is not a win by itself and `it reads, we cannot` is not automatically a fault: a
sample can be a file the other tool has misidentified, which has happened with all three. The
per-extension breakdown at the end is there to be looked at, not totalled.

Three distinctions are built into the comparison, each because leaving it out produced confident
nonsense:

- A PNG of four bits a channel gives a PPM stating maxval 15, whose samples run 0-15. Compared
  against full-range ones, every such picture reads as different — and that is most of what RECOIL
  writes.
- These machines had non-square pixels and the tools disagree on correcting for it, so one drawing
  the same picture at an integer multiple of another's size is not a disagreement about the picture.
- `same picture, other colours` counts decodes that agree on every pixel and differ only in what RGB
  each of the machine's colours is drawn as. What a hardware colour "is" was measured off a CRT
  rather than defined, and nobody measured the same; against RECOIL this is 121 samples, which
  counted as disagreements buried the 44 that are genuinely different pictures. It also covers the
  case where one tool's palette holds the same colour twice, so a picture that distinguishes them
  comes back with one fewer — allowed for a single pair only, since a decode collapsed to one flat
  colour would otherwise map consistently onto anything.

## Where the remaining differences with RECOIL are

Forty-four samples are genuinely different pictures rather than different shades. There is no cluster
left: the largest group sharing a decoder is two, and most are one apiece. Nine were closed by
finding shared causes — four ways an IFF states a palette that changes down the screen, and the
inverse-video register in every Interlace Character Editor mode — and that exhausted the families
that had one.

By the shape of the difference:

| shape | samples |
|---|---|
| geometry differs in a way that is not a clean multiple | 21 |
| same size, several colours wrong at once | 15 |
| RECOIL draws twice the width | 6 |
| RECOIL draws twice the height | 2 |

What is worth knowing before starting on any of them: **none is a single constant colour offset any
more.** That shape — every differing pixel out by the same amount — is what made the ICE modes
solvable, because doubling the offset gives the difference between two registers and names the one
at fault. Every remaining sample shows between seven and sixteen distinct offsets, so several things
are wrong at once in each, and the arithmetic that worked will not.

## How many formats are actually supported

The registry lists 741 formats and claims to read all of them. That number means much less than it
looks, and JPEG XR is why it is worth saying so: it parsed containers, decoded nothing, and was
counted the whole time. Its round-trip tests passed because the writer stored pixels and the reader
had a fallback that copied compressed bytes back out — two halves of the same misunderstanding
agreeing with each other.

Counting instead by what can be shown, over the 320 formats that have a sample here:

| | formats |
|---|---|
| verified against a third-party tool | 240 |
| decode, but disagree with the tool | 57 |
| decode, but no tool here reads the file to check against | 22 |
| refuse their own samples | 1 |

The other 421 registered formats have no sample in this corpus at all. Nothing is known about them —
they may be right, they may be JPEG XR. That is not a claim that they are broken; it is a claim that
"741 formats" and "240 formats shown to work" are different statements, and only the second is
evidence.

## Getting more samples

```sh
Tools/parity/fetch-samples.sh < list-of-format-directories
```

The corpus was the limit on what could be said, so it was widened: 345 further samples covering 320
extensions the old one had none of. What they show is worth stating plainly, because it moves the
answer the wrong way.

Of those 345, a third-party tool reads 83. We agree with 10 of them, differ on 9, and **cannot read
64**. So widening the corpus by half found sixty-four new gaps and ten new agreements.

That is the honest shape of it: the old corpus was drawn largely from formats we already read, and
72% coverage against it was not 72% coverage of the formats that exist. Anyone quoting a coverage
figure should say which corpus it is against.

## Camera raw, and why comparing it is not like comparing a picture

Widening the corpus turned up eleven camera raw samples, and they need reading differently from the
rest. A raw file holds sensor readings, not a picture: what a tool shows is either an embedded
preview the camera made, or the tool's own demosaicing of the sensor. Two tools disagreeing about a
raw is the normal case, not a fault.

Where we stand, after making four of them readable:

| file | we show | XnView shows | why |
|---|---|---|---|
| Olympus ORF | 3360x2504 | 2504x3340 | it crops to the active area and turns the picture upright |
| Panasonic RW2 | 1920x1440 | 4016x3016 | it demosaics; we show the embedded preview |
| Pentax PEF | 3008x2000 | 3040x2024 | same, and it crops differently |
| Fujifilm RAF | 1440x960 | 3032x2035 | same |
| Nikon NEF | 3904x2616 | 3900x2616 | agreed but for the crop |
| Sony ARW | 4928x3280 | 4928x3280 | both demosaic, and differ by 110 of 255 a channel |
| Epson ERF, Kodak KDC | small thumbnail | full picture | no JPEG anywhere in them; the full picture is sensor data |
| Kodak DCR | refused | full picture | carries JPEGs, none of which decode here |

The Sony line is the one to be careful about. Both tools demosaic and the results differ enormously,
because white balance, the colour matrix and the gamma curve are all choices rather than facts. That
is not evidence either is wrong, and it is why a raw cannot be scored the way a PNG can.

## Which read gaps are worth attempting

Sixty-two samples are read by RECOIL and not by us. They are not equally tractable, and the cheap
test for it is whether the file's length matches an uncompressed picture at the size RECOIL draws:

```sh
# length - small header == width * height * bpp / 8, for bpp in 1, 2, 4, 8, 24
```

Twenty-one of the sixty-two match that. The rest are compressed, or stored at a size nothing in the
file states, and each needs its coding worked out before anything else.

That filter is necessary and nowhere near sufficient. Eleven of the twenty-one were then read at the
layout their length implies and checked against RECOIL's rendering: ten decode to a single colour
where RECOIL draws two or more, which is to say they do not decode at all. A matching length says
only that the arithmetic is possible.

The trap in measuring this is worth stating, because it has now appeared three times in different
guises. Scoring a candidate by how consistently each stored value maps to one of the other tool's
colours reads as 98% agreement when a picture is mostly black and every value maps to black. The
guard is to require as many distinct colours out as the picture actually has in it.

What has come out of attempting them, which is the more useful half:

- A **coding** yields to arithmetic. Reconstruct the picture the other tool draws, and the file has to
  be a coding of exactly those bytes — that gave up the Atari Paintworks packing, the ICE inverse
  register, and the Mad Studio default colours, each verified to the byte.
- An **ordering** does not. There is nothing to invert: either the scan pattern is recognised or it is
  not. ComputerEyes and MAG both stalled there, and in both the first pixel of the wrong guess is
  exact, which is what makes them tempting.
- A file with **no header** cannot be verified from one sample. `.cut` decodes at 96 by 99 to the
  pixel, but nothing in it states either number, so a reader would be a guess dressed as a fact.

## What is left against IrfanView

Five samples, the smallest gap of the three installed tools, and every one now identified:

| sample | what it really is |
|---|---|
| `.fpr` | four bytes, then a complete PNG at 76 by 105 — which is what IrfanView draws |
| `.hpi` | its own format behind a PNG-like signature, `89 48 50 49`, and compressed |
| `.mif` | likewise, `MIMG` with chunks named as PNG names them |
| `.psf` | `FSPA`, stating 922 wide and 24 bits a pixel in plain little-endian, and compressed |
| `.pict` | a QuickDraw picture using records this does not follow |

None is a missing format in the sense of nobody having written one — three are compressed formats of
their own, and the `.fpr` is a PNG wearing four bytes of hat.

That last is deliberately not read. Teaching the PNG reader to find its signature a few bytes in
would open this file and put the most used format in the library at risk of claiming anything that
merely contains a PNG. One sample is not worth that, and no other reader can be given the rule
either without knowing whether a Fun Photor file legitimately wraps a PNG or this one is simply
misnamed.

## Decodes that succeed and are still wrong

```sh
dotnet run --project Tools/parity/Decode -- --implausible "$samples"
```

A reader taking its size from the wrong offset still reports success. One 6998-byte sample was read
as 150192 by 22341 — three and a third billion pixels — and nothing downstream questions it, so a
viewer asked to open that file tries to allocate for it. This lists decodes stating a size no format
here draws.

It is a floor, not a ceiling: it catches sizes that are obviously impossible, and a reader can still
be wrong within plausible bounds. Only the format's own validation catches those.

## Tom's Editor

```sh
Tools/parity/toms-coverage.sh
```

It is a web service with a daily conversion limit, so it cannot be swept over a corpus the way an
installed tool can. Its catalogue page costs nothing against that limit though, and answers the
coverage half on its own.

As measured: it lists 574 dotted tokens against our 1113 extensions, and of the 77 it lists that we
do not, most are not raster formats — page words the regex catches, ImageMagick's pseudo-formats
(`.gray`, `.png24`), vector and page-description formats, fonts, video, and braille embossing. What
is left is six: `.crw`, `.mrw` and `.x3f` camera raw, `.sid` and `.mrsid` wavelet, and `.sr7`.

The other half — whether we decode those formats the way it does — needs a conversion apiece, and
the limit stops that after a handful. Run the conformance suite with `TOMSEDITOR` set to spend the
day's allowance on formats no installed tool can judge.

## Reader gaps

```sh
dotnet run --project Tools/parity/Decode -- --why "$file"...
```

Every format claiming that file's extension is asked to read it and made to say why it would not.
The ordinary decode answers null for a wrong length, a foreign signature and an unimplemented depth
alike, and those are three different jobs.

Sweeping the corpus with RECOIL and comparing turned up 44 formats where it reads a sample and we do
not, or where we both read one and disagree. Two of those disagreements were the sweep's own fault:
it normalised our size to the reference tool's with `-resize`, which interpolates, when a machine
doubling a pixel is `-sample`. Use nearest-neighbour when comparing a picture stored 160 across and
shown 320.

Three of the refusals are not defects. RECOIL means a different format by that extension, and ours
is right to turn the file down:

| Extension | Ours | What the sample actually is |
|---|---|---|
| `.cpi` | Calamus (Atari ST) | Marco Pixel Editor (Atari 8-bit) |
| `.mpl` | IFF multi-palette (Amiga) | Mad Studio multi-colour player (Atari 8-bit) |
| `.pic` | Psion PIC | Graphic Arts Department (Atari 8-bit) |

Those are three formats missing rather than three readers broken, and a reader that accepted them
would be claiming files that are not its own.

What the rest turned out to need, where it has been established:

- **Compressed where we assume plain.** `.ufl`, `.cfli`, `.fbi`, `.him`, `.ghg` and others are far
  smaller than the screen they hold — 756 bytes against 19002 in one case. The reader models the
  unpacked screen and no unpacker exists.
- **Interlaced.** `.rip` renders in 79 colours, which sixteen hardware colours cannot do without two
  frames being blended. A solver for single-frame layouts cannot match one of these, and the same
  goes for the `.drl`, `.hlf` and `.ist` disagreements.
- **Speccy eXtended Graphics.** The pixels are settled: an 18-byte header stating width and height at
  bytes 8 and 10, a 508-byte table, then four bits a pixel high nibble first, which reproduces
  RECOIL's picture exactly as an index pattern. Where the palette lives is not settled — the sixteen
  colours it uses appear nowhere in the file as bytes or as nibbles, in any channel order.
- **FLI Graph.** 896 candidate arrangements of bitmap, eight matrices and colour memory were tried
  against the sample and none reproduces it.

### Which machine a reader thinks it is

A cheap check that finds a class of error no amount of tuning the layout would: take the colours the
reference tool used and see which machine's palette they belong to.

```sh
# every colour RECOIL drew, against the sixteen of the C64 and the 238 of the Atari
```

Four readers model the Commodore 64 while their samples are drawn entirely in Atari colours, and
RECOIL names all four as Atari 8-bit formats:

| Format | Colours drawn | In the C64's sixteen | In the Atari's |
|---|---|---|---|
| Interlace Studio (`.ist`) | 7 | 2 | 7 |
| Mcs (`.mcs`) | 8 | 2 | 8 |
| Rocky Interlace (`.rip`) | 79 | 1 | 8 |
| Din (`.din`) | 9 | 1 | 3 |

Every colour of the first two is an Atari colour. Those readers cannot be made right by moving an
offset: the palette they draw from is the wrong machine's, so the layout search that assumes a C64
screen was looking for something that is not there.

The same check explains a second family. Pixel Perfect draws 120 colours, Fun Painter and True Paint
90 each, and Ffli 69 — all within the C64's, which sixteen colours cannot do on their own. That is
two frames shown alternately and blended by the eye, and the reference tool averages them. Our
readers return one frame, which is why they disagree without being obviously wrong anywhere. An
interlaced format needs both frames decoded and averaged before it can be compared at all.

### Rocky Interlace, as far as it goes

The container is settled and the picture is not. A file opens with `RIP1.0  `, then two-byte
big-endian fields: the header size at 10, the line length at 12 and the height at 14. Chunks follow
from offset 17, each a one-byte length, a name ending in a colon, and that many bytes of data — a
`T:` title and a `CM:` colour map of nine registers in the sample. The chunks end exactly on the
stated header size, and what follows is the stated line length times the height to the byte:
44 + 80 × 200 = 16044, which is the file.

Our reader reads none of that. It takes the first two bytes as a load address, which this format
does not have, and the rest as an undifferentiated screen.

The pixels resisted. The picture has real detail at 320 across — only 52% of aligned pixel pairs are
uniform, so it is not 160 or 80 doubled — and 79 colours, which two blended frames of a four-colour
mode could produce. But no arrangement tried reproduces it: two 40-byte frames side by side on each
line or stacked whole, at one, two or four bits a pixel, with the second frame level with the first
or half a pixel either side of it. Either the graphics are compressed despite the length arithmetic
closing, or the mode is not one of those.

### ComputerEyes, as far as it goes

The sample opens with `EYES`, is 192022 bytes, and RECOIL draws it 320 by 200 in 13767 colours. The
arithmetic is exact: 22 + 3 × 64000. Every byte of the data is 0..63, so a sample is six bits, and
RECOIL's colours are those six bits at full scale — its first pixel (199, 178, 154) is (49, 44, 38)
of 63.

That is as far as it goes. The three planes look as though they start at 22, 64022 and 128022 —
the first two pixels come out exactly right from there — but the third does not, and taking the
whole picture that way has a value drawn two different ways in 62000 of 64000 pixels. Reversing the
rows, or dealing the even ones before the odd, does not help. Something reorders the samples and it
is not any of those.

A reader was not written on the strength of two pixels agreeing.

### What is left, and why each is hard

Of the twenty formats RECOIL reads and we did not, four have been fixed and three were never defects.
The remaining thirteen fall into four kinds, and the kind decides what it would take:

**Packed with an undocumented scheme** — UFLI (7757 bytes against 17194), Flip64, MegaPaint, Apple
SHR, Spectrum 512 smooshed. The screen these unpack to is understood; the packing is not. Flip64 and
Apple SHR are the clear cases: their samples are 756, 756, 2309 and 4508 bytes, and 7635, 7635, 15872
and 7844, and a format whose files differ in length for pictures of one size is packing them.

**Fixed size, layout unknown** — CFLI Designer and Interlace Studio were counted as packed and are
not. Three distinct CFLI pictures are 8170 bytes each and three distinct Interlace Studio ones are
17184 each, and a length that does not move with the picture is not compression. They are plain
layouts nobody has worked out: a hires-FLI search over every bitmap and screen offset in the CFLI
file, at five left margins and five screen strides, matched nothing. A run-length guess was tried on the Apple
one and consumes its input exactly while producing 26855 bytes rather than 32768, and no permutation
of the four PackBytes modes reaches it.

**Drawn on the wrong machine** — Interlace Studio, Mcs, Rocky Interlace, Din. Their readers model the
Commodore 64 and the colours are Atari's. These need the Atari screen modes implemented, not an
offset moved.

**Two frames blended** — ZX MultiArtist draws 26 colours of which 2 are pure machine colours, and
Multi-Lace Editor 10 of which 4 are. Neither can be matched a frame at a time, and searching two
frames over the plausible offsets found nothing, which suggests they are packed as well.

**Layout unsolved despite the container being known** — Rocky Interlace (header and chunks settled,
pixels not), ComputerEyes (size, depth and colour scale settled, sample order not), SXG (pixels
settled, palette not), FLI Graph (none of 896 arrangements).

One constraint is worth stating because it shapes all of this. RECOIL is the only complete reference
implementation of most of these formats and it is GPL-2.0-or-later, while this project ships
LGPL-3.0-or-later. It can be used as a black box — given a file, asked what picture comes out, and
that is how every fix here was found and checked — but its decoders cannot be read and ported. So
each of these has to be solved from the files themselves, and a format whose packing is not
self-evident from two or three samples stays unsolved until more of them turn up.

### Comparing samples against each other

Where a format has several samples of one length, the bytes that are identical in all of them are
structure and the bytes that differ are picture. That locates a layout without decoding anything, and
it is the cheapest thing to try on a format that has resisted a solver.

It settled CFLI Designer's container in one pass. The file is a load address and then eight blocks of
1000 bytes at a stride of 1024, the last without its padding: 2 + 7 × 1024 + 1000 = 8170, which is
the length of all three distinct samples to the byte. The 24 bytes of padding after each block are
nought in every sample, which is what identified them as padding rather than data.

What those blocks hold is still open. Eight blocks of a thousand at a 1024 stride is exactly how a
FLI picture stores its eight video matrices, and their contents look like matrix entries — nibble
pairs such as 0xF1 and 0x11. But eight matrices account for the whole file and a FLI needs a bitmap
as well, and the picture RECOIL draws uses both nibbles of an entry, so it is not colour alone
either: rendering every pixel from the high nibble, or every pixel from the low one, at either
margin and at both cell widths, is inconsistent in all eight combinations.

Interlace Studio's samples are equally uniform in length and much less uniform inside — the constant
regions are only at 8016..8207 and past 16208, which suggests two pictures with a header between
them rather than a block structure.
