# Codecs whose tables are not in the file

Five codecs were investigated and none was implemented. That is a result rather than a gap, and it is
written down here so the work is not repeated by somebody who assumes it was never attempted.

Indeo 3 (`IV32`), Indeo 4 (`IV41`), Indeo 5 (`IV50`), TrueMotion 1 (`DUCK`) and TrueMotion 2 (`TM20`)
are the proprietary codecs of the multimedia era. None has a published specification. No encoder for
any of them exists in ffmpeg, so no sample can be made here; samples were fetched from
`samples.ffmpeg.org` under `V-codecs/IV32/`, `IV41/`, `IV50/`, `DUCK/` and `TM20/`, and ffmpeg decodes
all five, so it remains a usable oracle. Nothing was committed.

## The argument that settles four of them

Every one of these codecs needs large constant tables — vector-quantisation dyads, delta and vector
tables, quantisation matrices, default variable-length code sets. There are two places such a table
can come from: the file, or the decoder. If it is in the file, the format can be recovered by reading
files. If it is in the decoder, it can only be recovered from somebody else's implementation, and
this project does not transcribe implementations — a copied decoder is a licence problem and teaches
nothing about whether the result is right.

So the question worth asking before writing any code is whether the tables are in the stream. The
smallest frame in each corpus answers it:

| codec | geometry | frames measured | smallest frame |
| --- | --- | --- | --- |
| Indeo 3 | 152x116 – 320x240 | 131–1073 | **340 bytes** |
| Indeo 4 | 240x180 – 360x288 | 904–15363 | **14 bytes** |
| Indeo 5 | 240x180 – 320x240 | 40–782 | **2 bytes** |
| TrueMotion 1 | 288x144 – 336x168 | 430–3196 | **0 bytes** |
| TrueMotion 2 | 320x224, 320x240 | 30–1791 | 92 bytes |

A 340-byte frame for a 320x240 picture, a 14-byte frame, a two-byte frame and an empty frame cannot
carry a quantisation table between them. Those tables are in the codec binary. Four of the five are
therefore not implementable here, and no amount of further effort changes that.

Indeo 3 was mapped anyway, because it was first and because the mapping is worth having if the
question is ever revisited. Its header is fully recovered and verified over roughly 3000 frames in
seven files: a 16-byte outer header (frame number, checksum, the magic `FR` = `0x4652`, data size),
then a 32-byte inner header carrying height at +12 and width at +14 as unsigned 16-bit values — both
confirmed against each file's `BITMAPINFOHEADER` — three plane offsets relative to absolute 16, and
then a 16-byte table:

```
02 14 26 38 4a 5c 6e 7f 82 94 a6 b8 ca dc ee ff
```

byte-identical in every file that parses. Those sixteen bytes are the whole of the constant-table
content the format carries. The dyad correction tables it needs to reconstruct a picture are not
among them. Decoded luma is uniformly even — 115 distinct values, every one even, across 151 frames —
so the codec works at seven bits of luma, which is itself a fact worth recording.

## TrueMotion 2 is the exception, and is two-thirds recovered

TM2 carries its own Huffman trees and delta tables, so it is self-describing and it *is* implementable
from files alone. It was taken as far as the entropy layer, using ffmpeg's decoded output as a
black-box oracle and never its source.

Verified, with the evidence:

  - **Packet**: 24-byte header; magic `00 00 01 01`; `(size-8)/4` as big-endian 24-bit at +5;
    `size*2` as little-endian 24-bit at +8; **width x 8** at +12 and **height x 4** at +14.
  - **Exactly eight length-prefixed streams** follow, each a 32-bit dword count then payload —
    verified on 2241 of 2242 packets across five files. The one exception is the final packet of a
    file whose RIFF size exceeds its actual length, which is a truncated download rather than a
    format variant.
  - **Each stream** is a count then a chain of blocks, each a 24-bit length in dwords plus a tag byte:
    `0x00` data, `0x40` Huffman table, `0x80` extra, and a word whose top byte is `0xFF` as a
    four-byte marker meaning "reuse the previous delta table". All 13941 streams parse with the chain
    covering them exactly.
  - **Huffman table block**: alphabet size, node count, and a third word whose top five bits give the
    symbol width; tree bits read most-significant-first within little-endian dwords, `0` introducing a
    leaf and its symbol, `1` an internal node. The node count is odd in all 13941 cases, so leaves are
    `(N+1)/2`. **Every one of the 13941 streams decodes exactly**, each consuming its token block to
    within the 0–31 bits of dword padding.
  - **Block type to stream allocation**, solved exactly with zero residual over 2241 frames: type 0
    takes 16 luma and 8 chroma tokens, type 1 takes 16 luma and one chroma pair, type 2 takes 4 luma
    and one chroma pair, types 3 and 5 take none, type 4 takes a 24-value update, type 6 takes two
    motion tokens.
  - **Type 5 means unchanged** — 100% separation against the oracle's changed-block map on every frame
    tested.
  - **Colour**: green is luma, blue is luma plus the first chroma value, red is luma plus the second,
    with chroma constant across each 2x2 quad. RGB-native, so the chroma-siting caveat that applies to
    every 4:2:0 codec here does not arise.
  - **Delta table** is centred and doubling — index 12 is zero, then plus and minus 2, 4, 8, 16, 32,
    64. Chroma uses the same table, with type-0 tokens ordered V then U per quad.
  - **Prediction** is planar: `Y[x,y] = Y[x-1,y] + Y[x,y-1] - Y[x-1,y-1] + delta`, exact across all
    four position classes over 66,080 samples including block edges, for types 0 and 1.
  - **Motion vectors**: one token gives the horizontal component and the next the vertical; symbol 3
    is -2 across 4139 occurrences and symbol 4 is +2 across 5100.

What remains: block types 2 and 3 — 11% of intra blocks — do not follow the planar rule at their top
row and left column, and their residuals there are plus or minus 1 and 3, values absent from the delta
table, which points to an interpolation with rounding rather than a coded delta. The inter path for
types 4 and 6 and the bit packing of the delta-table block are also unfinished.

A decoder handling only types 0 and 1 was deliberately not shipped. A wrong type 3 in a still passage
is indistinguishable from the codec working, which is the failure mode this project has spent a long
time removing from other readers.
