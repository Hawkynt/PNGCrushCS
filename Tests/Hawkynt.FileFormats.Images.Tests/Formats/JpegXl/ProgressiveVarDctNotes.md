# Progressive VarDCT test notes

Implementation behavior was derived from ISO/IEC 18181-1 and libjxl's BSD-3-Clause reference implementation, specifically FrameHeader `Passes`, `ProcessACGlobal`, `DecodeGroupImpl`/`DecodeACVarBlock`, and `ModularStreamId::ModularAC`.

No expressive libjxl implementation code is copied or translated. The two test vectors use generated pixels and libjxl only as encoder/interoperability oracle.
