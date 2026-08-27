# License-compatible reference sources for blocked codecs

`undecodable-codecs.md` deliberately treats implementation-only knowledge as unavailable because copying
an implementation without establishing its licence would turn a reverse-engineering problem into a
provenance problem. That is the right default, but it is stricter than necessary where the implementation
itself is distributed under terms compatible with this package's LGPL-3.0-or-later licence.

This file records sources that clear that additional gate. It is a source-provenance map, not a claim
that each codec is small or mechanically portable: the resulting C# still has to fit this package's
managed-code model and be verified against real streams before the corresponding negative result can be
removed from `undecodable-codecs.md`.

## Directly useful sources

| Codec | Compatible reference | Licence in source | What it unlocks | Suggested scope |
| --- | --- | --- | --- | --- |
| MSZH | [FFmpeg `libavcodec/lcldec.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/lcldec.c) and [`lcl.h`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/lcl.h), Roberto Togni | LGPL-2.1-or-later | The unpublished literal/back-reference coder, raw-frame fallback and two-section packet form | Port the coder into the already implemented LCL wrapper; start with independently verified RGB24 |
| Escape 124 | [FFmpeg `libavcodec/escape124.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/escape124.c), Eli Friedman | LGPL-2.1-or-later | The exact skip-count code that the published prose only calls “Rice decoding” | Reuse the existing ARMovie/RPL framing work and port the skip decoder plus codebook walk |
| Microsoft Screen 1 | [FFmpeg `libavcodec/mss1.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mss1.c) plus shared `mss12.*`, Konstantin Shishkov | LGPL-2.1-or-later | Arithmetic coder, model updates and tile reconstruction omitted from Microsoft's public API documentation | Port the self-contained MSS1 arithmetic path before considering MSS2 |
| Microsoft Screen 2 | [FFmpeg `libavcodec/mss2.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mss2.c) plus shared `mss12.*` | LGPL-2.1-or-later | The implementation-defined screen-coding layer and its VC-1-derived pieces | Larger port; share the MSS1 infrastructure rather than duplicating it |
| Lagarith | [FFmpeg `libavcodec/lagarith.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/lagarith.c), Nathan Caldwell | LGPL-2.1-or-later | A complete integer decoder that avoids having to reproduce the original encoder's floating-point probability construction | Port decode semantics, then compare packed native output against real Lagarith files; do not require reproducing the original encoder |
| Canopus HQ/HQA | [FFmpeg `libavcodec/hq_hqa.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/hq_hqa.c) plus `hq_common.*` / `hq_hqadata.h` | LGPL-2.1-or-later | Quantisation, VLC and transform tables absent from vendor white papers | Treat HQ and HQA together around shared tables and transform code |
| Canopus HQX | [FFmpeg `libavcodec/hqx.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/hqx.c) | LGPL-2.1-or-later | HQX VLC/tables and reconstruction rules absent from public vendor material | Separate decoder, but share Canopus colour/transform helpers where representations match |
| VP7 | [FFmpeg `libavcodec/vp8.c`](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/vp8.c) and its VP7/VP8 data helpers | LGPL-2.1-or-later | The dequantisation and motion-vector state that On2's VP7 document names but does not print | Extract VP7-only constants and behaviour instead of importing the VP8 decoder wholesale |

## Especially small unblock: Escape 124 skip counts

The missing Escape 124 value is not a generic Rice code. FFmpeg's LGPL decoder makes it a three-tier
prefix value:

1. Read one bit. Zero means a skip count of zero.
2. For one, add a three-bit value. Any result below eight is final.
3. At eight, add a seven-bit value. Any result below 135 is final.
4. At 135, add a twelve-bit value.

That is at most 23 bits and exactly fills the narrow hole recorded in `undecodable-codecs.md`; the
remaining Escape 124 structures in FFmpeg are likewise in the same LGPL source file. The important
porting detail is that FFmpeg declares the bit reader little-endian for this codec, so translating the
steps without translating bit order would produce a plausible-looking but wrong decoder.

## Why MSZH is the first conversion

MSZH has the lowest integration cost because this package already contains `LclHeader` and a verified
LCL ZLIB decoder. FFmpeg's compatible source supplies only the piece that was missing:

- a mask byte controls eight commands, most-significant mask bit first;
- a zero mask bit copies four literal bytes;
- a one mask bit reads a little-endian 16-bit descriptor, using eleven bits of backward distance and
  five bits of length, with the length measured in four-byte groups;
- overlapping back-references are legal;
- the multithread flag splits a packet into two independently compressed, equally sized output halves;
- an RGB24 packet whose byte length already equals the padded frame size is raw data even when the
  stream's compression field says MSZH.

`MszhVideoDecoder` is the first implementation produced from this map. Its source comment preserves the
upstream author and licence provenance and deliberately exposes RGB24 first, because that packing was
already independently measured by the sibling LCL ZLIB work.

## Licence rule used here

The package declares `LGPL-3.0-or-later`. Code under LGPL-2.1-or-later can be redistributed under a
later LGPL version, including LGPL-3.0, while retaining the upstream copyright and licence provenance.
MIT, BSD, ISC, zlib and public-domain sources are also normally usable with their required notices.
GPL-only code is deliberately excluded from this map: algorithms and factual format knowledge may be
reimplemented independently, but GPL-only source is not a direct conversion source for this LGPL
library.

For every port, keep the upstream copyright/source notice near the adapted code, keep the implementation
small enough that its provenance is obvious in review, and verify the result against real bitstreams
rather than treating licence compatibility as evidence of correctness.
