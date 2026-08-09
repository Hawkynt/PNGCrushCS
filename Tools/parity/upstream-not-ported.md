# What the published main has that this line does not

This tree and `origin/main` parted company 604 commits ago and grew the same code in
different directions. The remote's 29 commits are a run of fixes measured against
third-party decoders; almost all of that work exists here too, arrived at again rather
than merged, which is why not one of those commits matches anything here line for line.

The merge that reconciles the two takes this tree whole. That is deliberate: this line
is the one with the test suite behind it, and the remote's changes touch files that were
rewritten here since they were written there. Dropping the remote's version of a rewritten
file into this tree is not a merge, it is a swap, and one of them proved the point — the
remote's VIFF work inserts a member into `ViffMapType` and `ViffStorageType`, moving `Int`
from 3 to 4. The reader here was rewritten against the old numbering. Taking the enum
without the reader would have read every mapped VIFF file as the wrong type, silently,
and no test here covers it.

So the merge keeps this tree, and what the remote had that this tree does not is written
down here instead of being lost. The commits themselves are kept under the tag
`superseded/thirdparty-fixes-2026-08-08`.

## The twenty-one files

These existed before the split, the remote changed them, and this line never touched them.
Each is a fix this tree does not have.

  - `Formats/PalmPdb/PalmPdbFile.cs`, `PalmPdbReader.cs`, `PalmPdbWriter.cs` — the reader was
    written against a record layout the format does not use, and the detector recognised PDBs
    by a type nothing writes.
  - `Formats/Cineon/CineonHeader.cs`, `CineonWriter.cs` — a scan came back washed out and left
    again as a single channel; the transfer characteristic was not applied.
  - `Formats/Heif/HeifReader.cs`, `IsoBmffBox.cs` — a HEIF reported the size of its padding
    rather than the size of its picture, the clean aperture being ignored.
  - `Formats/JpegXl/JpegXlSizeHeader.cs` — an image came out as wide as it was tall whatever
    its real width, the ratio codes not being read.
  - `Formats/Eps/EpsReader.cs` — 106 lines, the largest of them.
  - `Formats/Viff/ViffMapType.cs`, `ViffStorageType.cs` — the enums are short a member against
    the struct they describe. **Not portable on its own**: see above.
  - `Formats/Wpg/WpgRleCompressor.cs` — the writer emitted raw rows where the format codes them.
  - `Formats/SunIcon/SunIconReader.cs` — one line.
  - `Formats/Ccitt/CcittChangingElements.cs` — folded into `CcittG4Decoder.cs` here, so this one
    is carried already and is listed for completeness.

And the tests that go with them: `EndToEnd.Tests/FormatRoundTripTests.cs`, and under
`Hawkynt.FileFormats.Images.Tests/Formats/`: `Cineon/CineonHeaderTests.cs`,
`Cineon/CineonWriterTests.cs`, `JpegXl/JpegXlSizeHeaderConformanceTests.cs`,
`Palm/PalmReaderTests.cs`, `PalmPdb/PalmPdbReaderTests.cs`, `PalmPdb/RoundTripTests.cs`,
`Tiff/RoundTripTests.cs`.

## How to port one

Not as a file copy. Take the fix's reasoning, check it against the reader that stands here
now, and measure it the way everything else here is measured — against a tool that is not
this project. A trial merge of the remote was run and left twenty-one tests failing, every
one of them a case where the remote's test met this tree's reader; that is the shape of
the work, and it is per-format rather than mechanical.
