# Parity report

Answers one question: is there anything RECOIL, XnView or IrfanView reads that we cannot read, or
read differently?

That is the question replacing them turns on. Counting how often we agree with a tool does not
answer it — agreeing on nine formats out of ten is no comfort if the tenth is one somebody has.

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

## Which read gaps are worth attempting

Sixty-two samples are read by RECOIL and not by us. They are not equally tractable, and the cheap
test for it is whether the file's length matches an uncompressed picture at the size RECOIL draws:

```sh
# length - small header == width * height * bpp / 8, for bpp in 1, 2, 4, 8, 24
```

Twenty-one of the sixty-two match that. The rest are compressed, or stored at a size nothing in the
file states, and each needs its coding worked out before anything else.

What has come out of attempting them, which is the more useful half:

- A **coding** yields to arithmetic. Reconstruct the picture the other tool draws, and the file has to
  be a coding of exactly those bytes — that gave up the Atari Paintworks packing, the ICE inverse
  register, and the Mad Studio default colours, each verified to the byte.
- An **ordering** does not. There is nothing to invert: either the scan pattern is recognised or it is
  not. ComputerEyes and MAG both stalled there, and in both the first pixel of the wrong guess is
  exact, which is what makes them tempting.
- A file with **no header** cannot be verified from one sample. `.cut` decodes at 96 by 99 to the
  pixel, but nothing in it states either number, so a reader would be a guess dressed as a fact.

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
