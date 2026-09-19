# Legacy video oracle survey

Modern codecs have reference models and conformance suites. Legacy codecs are harder: the best oracle
is often the original codec, an old standards test model, or a game/application engine. This survey
records additional sources that can break the current FFmpeg monoculture without pretending that a
second frontend is a second implementation.

As elsewhere in this directory, an entry is a **candidate behavioral oracle**, not a `[VerifiedBy]`
claim. Before counting any source as independent, inspect its lineage and then run it on the exact
bytes under test.

## Strong additional sources

| Codec/family | Oracle | Why it is useful | License / use boundary |
| --- | --- | --- | --- |
| Ut Video | original Ut Video Codec Suite by UMEZAWA Takeshi | Original implementation exposes Windows VCM/DMO paths and can encode/decode independently of our code | GPL-2.0-or-later; execute only as an external oracle |
| HuffYUV | Ben Rudiak-Gould's original HuffYUV 2.1.1 implementation | Original codec implementation is a much stronger compatibility target than another libavcodec frontend | GPL-2.0-or-later; execute only as an external oracle |
| DV | libdv | Independent MPEG/DV-era decoder/codec implementation, useful beside FFmpeg and OS codecs | GPL; execute as an external oracle |
| H.261 | ITU-T H.261 + OxideAV H.261 | Current pure-Rust implementation provides an implementation independent of libavcodec; standard supplies normative behavior | OxideAV is MIT; use behavior/tests, not implementation structure unless licensing review explicitly permits reuse |
| H.263 | ITU-T H.263 Appendix III TMN model + OxideAV H.263 | ITU publishes encoder/decoder implementation examples; the Rust implementation states it was built clean-room against H.263 | OxideAV is MIT; ITU material is reference/specification evidence |
| MPEG-1/2 Video | MPEG Software Simulation Group reference codec / TM5 + libmpeg2 | MPEG-2 Part 5 is reference software; libmpeg2 is an independent decoder with its own conformance-stream test suite | Behavioral oracles; libmpeg2 is GPL |
| id RoQ | original/id Tech 3 cinematic implementation | The game engine that shipped the format is an original behavior oracle for RoQ framing/decoding | id source is GPL; execute/compare behavior, do not transplant code into LGPL implementation |
| Westwood VQA | OpenRA plus independent VQA encoders such as ClusterVQA | Gives a non-FFmpeg application path and foreign encoder output for VQA | Audit OpenRA/encoder lineage before counting as independent; GPL code remains oracle-only |
| Flash / FLV family | Ruffle | Modern Flash Player reimplementation contains its own FLV/video subsystem and can be run headless for rendered-output comparison | MIT/Apache-2.0 project, but audit the backend selected for each video codec before counting it |
| Interplay MVE | original game corpus, `avi2mve` where obtainable, GemRB/ScummVM-family decoders | Multiple non-FFmpeg ecosystems exist and `avi2mve` can provide foreign encoded material | Per-tool lineage audit required; do not count translations of the same decoder twice |
| Sierra VMD | ScummVM/Coktel path plus original Sierra samples/applications | Useful non-FFmpeg compatibility target for the VMD family | Audit exact decoder provenance before counting it as independent |

## Sources

### Ut Video

The current upstream repository is the original author's codec suite and still documents its Windows
codec interfaces. This is precisely the kind of original implementation that should be captured in a
foreign corpus before old platform support disappears.

- https://github.com/umezawatakeshi/utvideo
- https://umezawatakeshi.github.io/utvideo/

### HuffYUV

The archived source identifies itself as HuffYUV 2.1.1 by Ben Rudiak-Gould. Its original VfW codec can
serve as an external Windows oracle for Huffyuv-compatible streams. FFVHUFF extensions must be tested
separately; the original codec is evidence for HuffYUV, not automatically for later FFmpeg variants.

- https://github.com/XhmikosR/huffyuv

### DV / IEC 61834 family

`libdv` is an independent DV video implementation. Combine it with published DV material and the
native Windows/Apple codec paths already listed in the main coverage matrix. Agreement across
libdv, an OS codec and FFmpeg is much stronger than an FFmpeg-only test.

- https://libdv.sourceforge.net/

### H.261

ITU-T H.261 is the normative source. OxideAV now provides a small, pure-Rust H.261 encoder/decoder
that is independent of libavcodec and explicitly covers I/P pictures, CIF/QCIF, motion compensation,
loop filtering and the H.261 IDCT-accuracy requirements. Use it as an additional black-box oracle,
not as the specification.

- https://www.itu.int/rec/T-REC-H.261
- https://github.com/OxideAV/oxideav-h261

### H.263

ITU-T H.263 Appendix III documents the TMN encoder/decoder model, including the optional annexes of
the later H.263 revisions. OxideAV's current H.263 project states that its decoder/encoder was built
clean-room from the ITU recommendation, giving us a useful independent implementation for the
baseline and the annexes it supports.

- https://www.itu.int/rec/T-REC-H.263-200106-I!App3
- https://www.itu.int/rec/T-REC-H.263
- https://github.com/OxideAV/oxideav-h263

### MPEG-1 / MPEG-2 Video

MPEG-2 Part 5 defines reference software. The historical MPEG Software Simulation Group codec includes
encoder/decoder software and TM5 material, while libmpeg2 supplies a separately developed MPEG-1/2
decoder. libmpeg2 also published conformance bitstreams with reference-output MD5s, which are valuable
fixed decoder tests.

- https://www.mpeg.org/standards/MPEG-2/
- https://libmpeg2.sourceforge.io/

### RoQ

id's released engine sources contain the cinematic/RoQ playback path and therefore act as an
original-application oracle. The source license is not suitable for copying into this LGPL library;
use it to run original files and compare outputs/packet behavior.

- https://github.com/ioquake/ioq3

### Westwood VQA

OpenRA supports the early Westwood games and their media ecosystem. ClusterVQA supplies a foreign VQA
encoder, including end-to-end tests, so it can produce streams our encoder did not write. Before
using either implementation as a formal second oracle, inspect the actual VQA codec source and its
provenance; project independence does not automatically prove codec-algorithm independence.

- https://github.com/OpenRA/OpenRA
- https://github.com/pekkavaa/ClusterVQA

### Flash / ScreenVideo candidates

Ruffle is a clean-room Flash Player replacement with distinct `flv` and `video` components and a
headless/exporter path. It is a promising oracle for Flash-era containers and codecs because it is
not merely a media-player UI. However, its selected backend must be checked per codec: if a codec is
forwarded to an external library, only that backend counts.

- https://github.com/ruffle-rs/ruffle

### Interplay MVE

There are several surviving engines and utilities around MVE. One current independent project records
that its MVE encoder was derived behaviorally by running the historical `avi2mve.exe` against a matrix
of synthetic AVI inputs, while its decoder was translated from GemRB. That is useful evidence that an
original encoder can still serve as a behavioral oracle, but the translated decoder is not an
independent implementation from GemRB and must not be double-counted.

- https://github.com/themuffinator/PakFu (id-format comparison context)
- https://github.com/ufoscout/infinitier (documents `avi2mve.exe` behavioral derivation and decoder lineage)

## Sources that must **not** be double-counted

- **ScummVM Smacker**: its source explicitly says it is based on FFmpeg's Smacker decoder, revision
  16143. ScummVM + FFmpeg is therefore one implementation lineage for Smacker, not two.
- **VLC/GStreamer using libavcodec**: different applications, same decoder engine.
- **Wrappers/bindings around libvpx/libaom/libpng/etc.**: different API surface, same engine.
- **AI or language ports translated from an existing decoder**: different source language, same
  implementation lineage unless behavior was independently derived from the specification.

## Operational rule

For each legacy codec task, start by filling a small evidence record:

```text
codec:
spec/reference document:
original/vendor implementation:
independent implementation A:
independent implementation B:
known lineage relationships:
foreign sample corpus:
foreign encoder available:
negative/malformed corpus:
```

If `independent implementation B` remains empty after a serious search, that is not a blocker to
implementing the format. It is a blocker to calling the result *two-oracle verified*. Keep it
`oracle-limited`, require stronger corpus/spec evidence, and make the limitation visible in the codec
notes.
