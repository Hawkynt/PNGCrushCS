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

Two corrections are built into the comparison, both because leaving them out produced confident
nonsense:

- A PNG of four bits a channel gives a PPM stating maxval 15, whose samples run 0-15. Compared
  against full-range ones, every such picture reads as different — and that is most of what RECOIL
  writes.
- These machines had non-square pixels and the tools disagree on correcting for it, so one drawing
  the same picture at an integer multiple of another's size is not a disagreement about the picture.

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
