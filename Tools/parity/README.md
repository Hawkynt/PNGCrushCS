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

## What it does not cover

Tom's Editor is a web service with a request quota, so it cannot be swept over a corpus and is not
part of this. It stays a spot-check.
