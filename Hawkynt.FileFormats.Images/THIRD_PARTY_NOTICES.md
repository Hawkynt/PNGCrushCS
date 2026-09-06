# Third-party codec provenance

This package contains managed ports/adaptations of permissively licensed reference-code algorithms. The resulting C# code is maintained in this repository; no native codec library is loaded at runtime.

## AV1 codec

`Formats/Avif/Codec` is a managed AV1 still-picture codec derived from two permissively licensed reference implementations.

**libaom** (BSD 2-Clause, Alliance for Open Media), pinned at tag `v3.15.0`, is the source of everything the AV1 specification leaves as a table and of the algorithms whose structure libaom fixes:

- Every constant table under `Formats/Avif/Codec/Tables` is transcribed from libaom's own source, value for value. `Av1DefaultCdfTables` and `Av1CoefficientCdfTables` come from `av1/common/entropymode.c` and `av1/common/token_cdfs.h`; `Av1ScanTables` from `av1/common/scan.c`; `Av1QuantizerTables` and `Av1QuantizerMatrixTables` from `av1/common/quant_common.c`; `Av1CoefficientContextTables` from `av1/common/txb_common.c`; `Av1IntraTables` from `av1/common/reconintra.{c,h}`, `aom_dsp/intrapred_common.h` and `av1/common/blockd.h`; `Av1StructureTables` from `av1/common/common_data.{c,h}` and `av1/common/blockd.{c,h}`; `Av1PostFilterTables` from `av1/common/cdef_block.c` and `av1/common/restoration.c`. A re-derived probability table is simply a wrong one, so these are copies rather than reconstructions.
- `Av1InverseTransform.cs` ports `av1_inv_txfm2d_add_c`, the `av1_idct*`/`av1_iadst*`/`av1_iidentity*` butterflies and `av1_highbd_iwht4x4_16_add_c`, with their cosine, shift and range tables.
- `Av1ForwardTransform.cs` ports `av1_fwht4x4_c`.
- `Av1IntraPrediction.cs` ports `build_intra_predictors` and the directional, smooth, Paeth, recursive-filter and chroma-from-luma predictors from `av1/common/reconintra.c`, `aom_dsp/intrapred.c` and `av1/common/cfl.c`.
- `Av1Deblocking.cs`, `Av1CdefFilter.cs` and `Av1LoopRestoration.cs` port the in-loop filters from `av1/common/av1_loopfilter.c`, `aom_dsp/loopfilter.c`, `av1/common/cdef{,_block}.c` and `av1/common/restoration.c`.
- The syntax and context rules in `Av1TileDecoder*.cs` follow `av1/decoder/decodetxb.c`, `av1/decoder/decodemv.c` and `av1/common/txb_common.h` alongside the specification's own section numbering; where libaom and the specification disagree the specification wins and the disagreement carries a comment.

**rav1e** (BSD 2-Clause) is the source of `Formats/Avif/Codec/Av1RangeEncoder.cs`: the storage and carry-propagation logic of the arithmetic writer follow rav1e's `src/ec.rs`.

Verbatim copies of the two files rav1e distributes with that code are kept beside it, in `Formats/Avif/Codec/RAV1E_LICENSE` and `Formats/Avif/Codec/RAV1E_PATENTS`. The second is not optional boilerplate: AV1 is patent-encumbered, and the Alliance for Open Media Patent License 1.0 is the grant under which an implementation may be distributed at all, so it travels with the code rather than being summarised here. libaom is distributed under the same pair of licences.

### libaom license (BSD 2-Clause)

Copyright (c) 2016, Alliance for Open Media. All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Source: https://aomedia.googlesource.com/aom/ (tag `v3.15.0`)

### rav1e license (BSD 2-Clause)

Copyright (c) 2017-2023, the rav1e contributors
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Source: https://github.com/xiph/rav1e

### Alliance for Open Media Patent License 1.0

Reproduced verbatim in `Formats/Avif/Codec/RAV1E_PATENTS`.

## JPEG XL fast-lossless encoder

`Formats/JpegXl/Codec/JxlFastLosslessEncoder.cs` is a managed adaptation of the JPEG XL project's fast-lossless modular encoder (`lib/jxl/enc_fast_lossless.cc`), cross-checked against the Rust `zune-jpegxl` adaptation.

### libjxl license (BSD 3-Clause)

Copyright (c) the JPEG XL Project Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Source: https://github.com/libjxl/libjxl

### zune-jpegxl license used for cross-checking (MIT option)

MIT License

Copyright (c) zune-image developers

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Source: https://github.com/etemesi254/zune-image/tree/dev/crates/zune-jpegxl

## JPEG XR reference-code port

`Formats/JpegXr/Reference` vendors the pure-managed JPEG XR codec core from `SharpAstro/Codecs`, `src/SharpAstro.Jxr`, pinned at commit `7cad99deda0e6c68f68e1c9c64d442c5b85d48a2`. SharpAstro describes that code as a faithful managed port of Microsoft's JXRLib reference implementation and validates it against JXRLib/Windows WIC. Hawkynt imports the T.832 codec core only; its T.833 container and `RawImage` adapters remain local code.

The pinned SharpAstro source is dedicated to the public domain under the Unlicense. A verbatim copy of that license and a pinned-source attribution note are kept beside the vendored files in `Formats/JpegXr/Reference/UNLICENSE` and `Formats/JpegXr/Reference/UPSTREAM.md`.

### SharpAstro.Jxr license (Unlicense / public domain dedication)

This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or distribute this software, either in source code form or as a compiled binary, for any purpose, commercial or non-commercial, and by any means.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Sources:
- https://github.com/SharpAstro/Codecs
- https://github.com/4creators/jxrlib
