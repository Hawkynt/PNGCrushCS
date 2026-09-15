The progressive VarDCT fixtures in this directory are generated test data.

Source pixels are a deterministic 64x48 RGB pattern generated specifically for these tests:
R=(13*x + 7*y + (x*y)%17) & 255
G=(3*x + 19*y + 5*(x^y)) & 255
B=(23*x + 5*y + (7*x + 11*y)%31) & 255

They were encoded with libjxl 0.11.1 through its C API at distance 1, effort 7, VarDCT mode, with patches/noise/restoration features disabled to isolate AC progression:
- libjxl_progressive_ac_64x48.jxl: JXL_ENC_FRAME_SETTING_PROGRESSIVE_AC=1
- libjxl_qprogressive_ac_64x48.jxl: JXL_ENC_FRAME_SETTING_QPROGRESSIVE_AC=1

libjxl is BSD-3-Clause. No external image or source-code material is embedded in these fixtures.
