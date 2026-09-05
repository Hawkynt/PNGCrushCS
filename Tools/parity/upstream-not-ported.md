# What the published main had that this line still does not

This tree and `origin/main` parted company and grew the same code in different directions. The
remote's 29 commits were a run of fixes measured against third-party decoders; almost all of that
work existed here too, arrived at again rather than merged, which is why not one of those commits
matched anything here line for line.

The merge that reconciled the two took this tree whole, and it has happened: it is `d890ce29`,
*Merge the published main, keeping this tree*, and
`git merge-base --is-ancestor superseded/thirdparty-fixes-2026-08-08 HEAD` now answers yes. Taking
this tree was deliberate. This line is the one with the test suite behind it, and the remote's
changes touched files that had been rewritten here since they were written there. Dropping the
remote's version of a rewritten file into this tree would not have been a merge but a swap, and one
of them proved the point — the remote's VIFF work inserts a member into `ViffMapType` and
`ViffStorageType`, moving `Int` from 3 to 4. The reader here was written against the old numbering.
Taking the enum without the reader would have read every mapped VIFF file as the wrong type,
silently, and no test here covers it.

So the merge kept this tree, and what the remote had that this tree did not is written down here
instead of being lost. The commits themselves are kept under the tag
`superseded/thirdparty-fixes-2026-08-08`.

## Four of the nine are now carried

Each was ported afterwards, on its own merits and with its own measurement, rather than by copying
the remote's file:

  - `Formats/Heif/HeifReader.cs`, `IsoBmffBox.cs` — a HEIF reported the size of its padding rather
    than the size of its picture, the clean aperture being ignored. Closed by #36, and #43 went
    further and made the reader refuse a HEIF it cannot decode rather than announce an empty one.
  - `Formats/JpegXl/JpegXlSizeHeader.cs` — an image came out as wide as it was tall whatever its
    real width, the ratio codes not being read. Closed by #18; the header reads the three ratio bits
    and spells the width out when the code is zero.
  - `Formats/Wpg/WpgRleCompressor.cs` — the writer emitted raw rows where the format codes them.
    Closed by #20; `WpgWriter` runs every row through `WpgRleCompressor.CompressRows`.
  - `Formats/SunIcon/SunIconReader.cs` — one line. Closed by #16, which stopped an XPM being
    detected as a Sun icon and then refused.
  - `Formats/Ccitt/CcittChangingElements.cs` was folded into `CcittG4Decoder.cs` here before the
    split and was never outstanding; it is listed because the original note listed it.

## Four are still open

These existed before the split, the remote changed them, this line never touched them, and it still
has not. Each is a fix this tree does not have.

  - `Formats/PalmPdb/PalmPdbFile.cs`, `PalmPdbReader.cs`, `PalmPdbWriter.cs` — the reader is written
    against a record layout the format does not use, and the detector recognises PDBs by a type
    nothing writes. It still matches `Img ` at offset 60; the type a Palm Image Viewer picture
    declares there is `vIMG`, with creator `View`, and the record inside opens with a 58-byte
    descriptor before any pixels. The picture is two bits a pixel, MSB first, and the width is
    always a multiple of sixteen. Nothing that produced a PDB can be read today, and nothing that
    reads one can use what this produces.
  - `Formats/Cineon/CineonHeader.cs`, `CineonWriter.cs` — a scan comes back washed out and leaves
    again as a single channel. Each colour is its own element, described by a 28-byte record of its
    own starting at offset 196, and `NumElements` says how many of those records are filled in. Only
    the first is modelled here, so the writer describes a one-channel image and leaves the other two
    records zero while writing three channels' worth of pixels. ImageMagick reads the result as a
    single channel and returns a cyan ramp.
  - `Formats/Eps/EpsReader.cs` — 106 lines, the largest of them.
  - `Formats/Viff/ViffMapType.cs`, `ViffStorageType.cs` — **the live trap, and the reason this file
    still exists.** Khoros' `VFF_TYP_*` and `VFF_MAPTYP_*` constants are not dense: for the integer
    types the constant is the width in bytes, which leaves 3, 7 and 8 unused. Both enums here are
    numbered 0..6 as if they ran consecutively, which puts `Int` on 3 and `Float` on 4 — and 4 is
    the value a real four-byte-integer file carries, so such a file is read as floating point. This
    is not a one-line change: `ViffReader`, `ViffWriter` and `Tests/…/Viff/DataTypeTests.cs` all
    assert the dense numbering, and `DataTypeTests` would have to be corrected rather than deleted.
    Only `Byte` is exercised against a third party, because ImageMagick writes nothing else, which
    is why no test catches it.

Every one of those files exists in both trees; what differs is what the remote's copy asserts. For
the rows still open, the assertions to take across are in the tag's versions of
`EndToEnd.Tests/FormatRoundTripTests.cs`, and under `Hawkynt.FileFormats.Images.Tests/Formats/`:
`Cineon/CineonHeaderTests.cs`, `Cineon/CineonWriterTests.cs`, `Palm/PalmReaderTests.cs`,
`PalmPdb/PalmPdbReaderTests.cs`, `PalmPdb/RoundTripTests.cs` and `Tiff/RoundTripTests.cs`. Read one
with `git show superseded/thirdparty-fixes-2026-08-08:<path>`.

## How to port one

Not as a file copy. Take the fix's reasoning, check it against the reader that stands here now, and
measure it the way everything else here is measured — against a tool that is not this project. The
trial merge of the remote left twenty-one tests failing, every one of them a case where the remote's
test met this tree's reader; that is the shape of the work, and it is per-format rather than
mechanical. The four already closed were each closed that way.
