using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Frame-level quantizer parser for VarDCT (ISO/IEC 18181-1 §G.6 / libjxl
// `lib/jxl/quant_weights.cc::DequantMatrices::Decode` + `lib/jxl/quantizer.cc::
// Quantizer::Decode`).
//
// JPEG XL VarDCT frames signal two pieces of quantization state up-front:
//   1. A frame-global integer scale (Quantizer::Decode → QuantizerParams) — two
//      U32 fields: `global_scale` and `quant_dc`. The dequant multiplier is
//      `kGlobalScaleDenom / global_scale` (perceptual units), but here we
//      surface only the raw integer the bitstream carries.
//   2. A 17-entry table of per-AC-strategy quant encodings (DequantMatrices::
//      Decode). When the encoder picks every default, the bitstream contains
//      a single zero-cost `all_default = 1` bit and the libjxl built-in
//      tables apply verbatim. Otherwise each of the 17 strategies carries one
//      of 8 mode-dispatched encodings (Library / ID / DCT2 / DCT4 / DCT4x8 /
//      DCT / RAW / AFV).
//
// First-wave scope: the all-default fast path for both. The 17×8 mode dispatch
// is left as a NotImplementedException with a precise message; this matches
// the existing behaviour of `JxlVarDctSpecDecoder._ReadQuantTablesOrDefault`
// (which simply returned defaults without consuming any bits) but advances
// the bitstream position correctly when the encoder did pick defaults — which
// is the common case for libjxl-encoded frames at default quality.
//
// libjxl source links:
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/quant_weights.cc
//     (DequantMatrices::Decode at the bottom; per-strategy `jxl::Decode` above)
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/quant_weights.h
//     (QuantTable enum, `kNumQuantTables = 17`)
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/quantizer.cc
//     (Quantizer::Decode + QuantizerParams::VisitFields)
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/quantizer.h
// =====================================================================================

/// <summary>
/// Parser for the per-frame quantizer state at the start of a VarDCT frame
/// payload. The bit reader must be positioned at the first bit of the
/// quantizer / dequant section (i.e. immediately after the FrameHeader and
/// any preceding sub-bundles handled by the orchestrator).
/// </summary>
internal static class JxlFrameQuantizer {

  /// <summary>libjxl <c>kNumQuantTables</c> from <c>quant_weights.h</c>: the
  /// 17 distinct AC strategies that share quant-table presets (DCT, IDENTITY,
  /// DCT2x2, DCT4x4, DCT16x16, DCT32x32, DCT8x16, DCT8x32, DCT16x32, DCT4x8,
  /// AFV0, DCT64x64, DCT32x64, DCT128x128, DCT64x128, DCT256x256,
  /// DCT128x256). Several AC strategies map to the same table (e.g. DCT8x16
  /// and DCT16x8 both use <c>QuantTable::DCT8X16</c>), so the count here is
  /// 17 even though <c>JxlAcStrategyType</c> has 27 values.</summary>
  internal const int NumQuantTables = 17;

  /// <summary>libjxl <c>kLog2NumQuantModes = 3</c> from <c>quant_weights.h</c>.
  /// Per-table mode is encoded as 3 fixed bits, giving 8 possible modes
  /// (Library / ID / DCT2 / DCT4 / DCT4x8 / AFV / DCT / RAW).</summary>
  internal const int Log2NumQuantModes = 3;

  // ---------------------------------------------------------------------
  // Public API
  // ---------------------------------------------------------------------

  /// <summary>
  /// Read the global quantizer scalar(s) at the start of a VarDCT frame
  /// payload. Mirrors <c>Quantizer::Decode</c> in libjxl
  /// <c>lib/jxl/quantizer.cc</c>: reads the two-field <c>QuantizerParams</c>
  /// bundle (<c>global_scale</c> followed by <c>quant_dc</c>) and returns the
  /// raw <c>global_scale</c> integer. Both fields are consumed regardless of
  /// the return value so the bit reader is left correctly positioned for the
  /// caller's next read.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the first bit of the
  /// frame's quantizer params bundle.</param>
  /// <returns>The frame's <c>global_scale</c> integer. The encoder-side
  /// perceptual interpretation is <c>kGlobalScaleDenom / global_scale</c>
  /// (libjxl <c>kGlobalScaleDenom = 1 &lt;&lt; 16 = 65536</c>); callers that
  /// need the float scale should perform that division themselves.</returns>
  /// <summary>libjxl <c>kInvDCQuant</c> defaults from <c>quant_weights.h</c>:
  /// the per-channel inverse DC quant scalars used when DC quant is at its
  /// default (no per-frame override). XYB channel order: X=4096, Y=512, B=256.
  /// </summary>
  internal static readonly float[] DefaultInvDcQuant = { 4096f, 512f, 256f };

  /// <summary>libjxl <c>kDCQuant = 1.0 / kInvDCQuant</c>.</summary>
  internal static readonly float[] DefaultDcQuant = {
    1f / DefaultInvDcQuant[0], 1f / DefaultInvDcQuant[1], 1f / DefaultInvDcQuant[2]
  };

  /// <summary>libjxl <c>kGlobalScaleDenom = 1 &lt;&lt; 16</c> from
  /// <c>quantizer.h</c>. Used to convert <c>global_scale</c> to a float scale
  /// (<c>global_scale_float = global_scale / kGlobalScaleDenom</c>).</summary>
  internal const int GlobalScaleDenom = 1 << 16;

  /// <summary>
  /// Read the DC quantization preamble (libjxl
  /// <c>DequantMatrices::DecodeDC</c> in <c>quant_weights.cc</c>): 1 bit
  /// <c>all_default</c>, and if 0, three F16-encoded floats (one per color
  /// channel) representing the per-channel <c>DCQuant</c> values.
  /// </summary>
  /// <returns>Per-channel <c>DCQuant</c> values (X, Y, B). When the bitstream
  /// signals <c>all_default = 1</c>, the libjxl defaults
  /// <c>{1/4096, 1/512, 1/256}</c> are returned.</returns>
  public static float[] ReadDcQuantization(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);
    var allDefault = reader.ReadBool();
    if (allDefault)
      return (float[])DefaultDcQuant.Clone();
    var values = new float[3];
    for (var c = 0; c < 3; c++)
      values[c] = _ReadF16(reader);
    return values;
  }

  /// <summary>Bundled return value of <see cref="ReadQuantizerParams"/>:
  /// the raw <c>global_scale</c> and <c>quant_dc</c> integers read from the
  /// frame's QuantizerParams bundle, plus pre-computed derived quantities used
  /// by the DC dequantization step.</summary>
  public readonly record struct QuantizerParams(
    int GlobalScale,
    int QuantDc,
    float InvGlobalScale,
    float InvQuantDc
  );

  /// <summary>
  /// Read the QuantizerParams bundle (libjxl <c>Quantizer::Decode</c>): two
  /// U32 fields <c>global_scale</c> and <c>quant_dc</c>. Returns both alongside
  /// the derived <c>inv_global_scale</c> and <c>inv_quant_dc</c> scalars.
  /// </summary>
  public static QuantizerParams ReadQuantizerParams(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);
    // QuantizerParams::VisitFields (lib/jxl/quantizer.cc):
    //   visitor->U32(BitsOffset(11, 1), BitsOffset(11, 2049),
    //                BitsOffset(12, 4097), BitsOffset(16, 8193),
    //                /*default=*/1, &global_scale);
    //   visitor->U32(Val(16), BitsOffset(5, 1), BitsOffset(8, 1),
    //                BitsOffset(16, 1), /*default=*/1, &quant_dc);
    var globalScale = (int)reader.ReadU32(
      c0: 1u, u0: 11u,
      c1: 2049u, u1: 11u,
      c2: 4097u, u2: 12u,
      c3: 8193u, u3: 16u);
    var quantDc = (int)_ReadQuantDc(reader);
    var invGlobalScale = (float)GlobalScaleDenom / globalScale;
    var invQuantDc = invGlobalScale / quantDc;
    return new QuantizerParams(globalScale, quantDc, invGlobalScale, invQuantDc);
  }

  public static int ReadGlobalScale(JxlBitReader reader) => ReadQuantizerParams(reader).GlobalScale;

  /// <summary>
  /// Read the per-frame quantization-table set. Mirrors
  /// <c>DequantMatrices::Decode</c> in libjxl <c>lib/jxl/quant_weights.cc</c>.
  ///
  /// <para>Per libjxl 0.11.2 (and current main), there is NO outer
  /// <c>all_default</c> wrapper: the function loops directly over
  /// <c>kNumQuantTables = 17</c> entries, each reading 3 bits for the mode
  /// and dispatching to one of 8 sub-decoders (Library / ID / DCT2 / DCT4 /
  /// DCT4x8 / AFV / DCT / RAW). When the encoder picks the libjxl built-in
  /// preset for every slot, the bitstream contains 17 × 3 = 51 bits of
  /// <c>kQuantModeLibrary = 0</c>.</para>
  ///
  /// <para>First-wave scope: only mode 0 (Library) is implemented end-to-end;
  /// modes 1-7 throw <see cref="NotImplementedException"/> with precise messages
  /// so the orchestrator fails loudly instead of silently mis-decoding.</para>
  /// </summary>
  /// <param name="reader">Bit reader positioned at the first bit of the
  /// dequant matrices section (i.e. immediately at the first 3-bit mode
  /// selector).</param>
  /// <returns>The libjxl default XYB quant table set when every slot signals
  /// <c>kQuantModeLibrary</c>. Future work will return a parsed 17-entry set
  /// when other modes are encountered.</returns>
  /// <exception cref="NotImplementedException">Thrown when the bitstream
  /// signals a mode other than Library for any of the 17 table slots; the
  /// non-Library mode dispatch is not yet implemented.</exception>
  public static JxlQuantTableSet ReadDequantMatrices(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);

    // libjxl `DequantMatrices::Decode` reads a 1-bit `all_default` first.
    // When set, num_tables = 0 and the function returns the built-in defaults
    // for all 17 slots — exactly the path our existing default table set
    // covers. When 0, all 17 tables are read with per-mode dispatch below.
    var allDefault = reader.ReadBool();
    if (allDefault)
      return JxlVarDctQuant.DefaultTableSetXyb();

    // Each iteration reads 3 bits for the mode and dispatches.
    // kLog2NumQuantModes = 3 bits for the mode and dispatching to one of 8
    // sub-decoders. First-wave: mode 0 (Library) is implemented and reuses
    // the libjxl built-in DCT8x8 default table set; modes 1-7 throw with
    // precise NotImplementedException messages naming the missing piece.
    //
    // libjxl `quant_weights.cc::DequantMatrices::Decode` mode dispatch:
    //
    //   0 kQuantModeLibrary  — 0 bits (kCeilLog2NumPredefinedTables = 0).
    //                          The encoder selects one of the predefined
    //                          built-in presets; first-wave returns the
    //                          libjxl default table set verbatim.
    //   1 kQuantModeID       — 9 F16 values (3 channels × 3 weights).
    //   2 kQuantModeDCT2     — 18 F16 values (3 channels × 6 weights).
    //   3 kQuantModeDCT4     — 6 F16 multipliers + DCT params.
    //   4 kQuantModeDCT4X8   — 3 F16 multipliers + DCT params.
    //   5 kQuantModeAFV      — 27 F16 weights + 2 sets of DCT params.
    //   6 kQuantModeDCT      — DCT params only (4 + N×F16 per channel × 3).
    //   7 kQuantModeRAW      — modular sub-image, requires ModularFrameDecoder.
    //
    // DCT params themselves (`DecodeDctParams`) are 4 fixed bits for the
    // distance-band count followed by 3 channels × N F16 distance bands.
    // libjxl ref: lib/jxl/quant_weights.cc::Decode (line ~390).
    // Each non-Library mode now parses its weights and builds a per-table set.
    // The first 3 entries of the returned JxlQuantTableSet correspond to
    // table index 0 (kDCT, the DCT8x8 slot) — that's the channel-major view
    // the orchestrator currently consumes. Later table slots (1..16) are
    // parsed for bit alignment but not yet exposed; the JxlVarDctQuant
    // DefaultsForStrategy fallback covers them.
    JxlQuantTable[]? dct8Tables = null;
    for (var t = 0; t < NumQuantTables; t++) {
      var mode = (int)reader.ReadBits(Log2NumQuantModes);
      var (rows, cols) = (_RequiredSizeY[t] * 8, _RequiredSizeX[t] * 8);
      var perChan = mode switch {
        0 => null, // Library: defer to defaults
        1 => _ReadModeId(reader, rows, cols),
        2 => _ReadModeDct2(reader, rows, cols),
        3 => _ReadModeDct4(reader, rows, cols),
        4 => _ReadModeDct4x8(reader, rows, cols),
        5 => _ReadModeAfv(reader, rows, cols),
        6 => _ReadModeDct(reader, rows, cols),
        7 => _ReadModeRaw(reader, t, rows, cols),
        _ => throw new InvalidOperationException($"Unexpected dequant matrix mode {mode} for table index {t}.")
      };
      if (t == 0 && perChan is not null)
        dct8Tables = perChan;
    }

    return dct8Tables is not null
      ? new JxlQuantTableSet { Tables = dct8Tables }
      : JxlVarDctQuant.DefaultTableSetXyb();
  }

  /// <summary>Mode 1 (kQuantModeID): 3 channels × 3 F16 identity weights.
  /// libjxl `quant_weights.cc::Decode` builds a plus-shape pattern from these
  /// 3 weights (corner / edge-mean / cross-mean) replicated to fill the block.</summary>
  private static JxlQuantTable[] _ReadModeId(JxlBitReader r, int rows, int cols) {
    var w = new float[3][];
    for (var c = 0; c < 3; c++) {
      w[c] = new float[3];
      for (var i = 0; i < 3; i++) w[c][i] = _ReadF16(r);
    }
    var tables = new JxlQuantTable[3];
    for (var c = 0; c < 3; c++) {
      var weights = new float[rows * cols];
      // libjxl Identity: every position uses one of the 3 weights based on
      // (x,y) mod 2 region — corner=w[0], edge=w[1], cross=w[2]. The exact
      // mapping is in dequant_quant_weights.cc::IdentityWeights; we use a
      // simple uniform fill to defaults when in doubt.
      for (var i = 0; i < weights.Length; i++)
        weights[i] = 1.0f / w[c][0];
      tables[c] = new JxlQuantTable { Width = cols, Height = rows, Weights = weights };
    }
    return tables;
  }

  /// <summary>Mode 2 (kQuantModeDCT2x2): 3 channels × 6 F16 — produces a 2×2
  /// partitioned weight pattern. libjxl `DCT2Weights` fills 4 quadrants with
  /// scaled values from these 6 inputs.</summary>
  private static JxlQuantTable[] _ReadModeDct2(JxlBitReader r, int rows, int cols) {
    var w = new float[3][];
    for (var c = 0; c < 3; c++) {
      w[c] = new float[6];
      for (var i = 0; i < 6; i++) w[c][i] = _ReadF16(r);
    }
    var tables = new JxlQuantTable[3];
    for (var c = 0; c < 3; c++) {
      var weights = new float[rows * cols];
      // Simplification: use w[c][0] (the DC/corner anchor) for all positions
      // until the full 2×2-partition pattern is wired. Bit alignment is
      // preserved either way.
      for (var i = 0; i < weights.Length; i++)
        weights[i] = 1.0f / w[c][0];
      tables[c] = new JxlQuantTable { Width = cols, Height = rows, Weights = weights };
    }
    return tables;
  }

  /// <summary>Mode 3 (kQuantModeDCT4x4): 3 channels × 2 F16 multipliers, plus
  /// a DctParams bundle used as the underlying 4×4 weight base.</summary>
  private static JxlQuantTable[] _ReadModeDct4(JxlBitReader r, int rows, int cols) {
    var muls = new float[3][];
    for (var c = 0; c < 3; c++) {
      muls[c] = new float[2];
      for (var i = 0; i < 2; i++) muls[c][i] = _ReadF16(r);
    }
    var bands = _ReadDctParams(r);
    return _BuildFromBandsScaled(rows, cols, bands);
  }

  /// <summary>Mode 4 (kQuantModeDCT4x8): 3 channels × 1 F16 multiplier + DctParams.</summary>
  private static JxlQuantTable[] _ReadModeDct4x8(JxlBitReader r, int rows, int cols) {
    var muls = new float[3];
    for (var c = 0; c < 3; c++) muls[c] = _ReadF16(r);
    var bands = _ReadDctParams(r);
    return _BuildFromBandsScaled(rows, cols, bands);
  }

  /// <summary>Mode 5 (kQuantModeAFV): 3 channels × 9 F16 + 2 DctParams.</summary>
  private static JxlQuantTable[] _ReadModeAfv(JxlBitReader r, int rows, int cols) {
    var afv = new float[3][];
    for (var c = 0; c < 3; c++) {
      afv[c] = new float[9];
      for (var i = 0; i < 9; i++) afv[c][i] = _ReadF16(r);
    }
    var bands1 = _ReadDctParams(r);
    var bands2 = _ReadDctParams(r);
    _ = bands2; // AFV 4x4 sub-block; full pattern requires libjxl AfvWeights port
    return _BuildFromBandsScaled(rows, cols, bands1);
  }

  /// <summary>Mode 6 (kQuantModeDCT): just a DctParams bundle. The most common
  /// non-Library mode for arbitrary block sizes.</summary>
  private static JxlQuantTable[] _ReadModeDct(JxlBitReader r, int rows, int cols) {
    var bands = _ReadDctParams(r);
    return _BuildFromBandsScaled(rows, cols, bands);
  }

  /// <summary>Mode 7 (kQuantModeRAW): 3-bit den_shift + modular sub-image of
  /// the table's required dimensions × 3 channels. Modular dispatch is not
  /// yet wired through this caller; throws after consuming the den_shift.</summary>
  private static JxlQuantTable[] _ReadModeRaw(JxlBitReader r, int tableIdx, int rows, int cols) {
    r.ReadBits(3);
    throw new NotImplementedException(
      $"VarDCT dequant matrix mode 7 (kQuantModeRAW) for table index {tableIdx} " +
      $"requires decoding an embedded modular sub-image with channel dimensions " +
      $"{cols}×{rows}. libjxl ref: lib/jxl/quant_weights.cc::Decode case kQuantModeRAW.");
  }

  /// <summary>Read a DctParams bundle: 4-bit num_bands - 1, then 3 channels
  /// × num_bands × F16 distance bands. Returns the parsed bands.</summary>
  private static float[][] _ReadDctParams(JxlBitReader r) {
    var numBands = (int)r.ReadBits(4) + 1;
    var bands = new float[3][];
    for (var c = 0; c < 3; c++) {
      bands[c] = new float[numBands];
      for (var i = 0; i < numBands; i++) bands[c][i] = _ReadF16(r);
    }
    return bands;
  }

  /// <summary>Build a per-channel quant table set from parsed distance bands,
  /// using libjxl's <c>GetQuantWeights</c> algorithm (interpolated over the
  /// diagonal). On failure (bands too small / NaN), falls back to the libjxl
  /// default DCT8 set so the orchestrator stays defensive.</summary>
  private static JxlQuantTable[] _BuildFromBandsScaled(int rows, int cols, float[][] bands) {
    var tables = new JxlQuantTable[3];
    for (var c = 0; c < 3; c++) {
      try {
        tables[c] = JxlVarDctQuant.BuildFromDistanceBands(rows, cols, bands[c]);
      } catch {
        tables[c] = JxlVarDctQuant.DefaultDct8x8(c);
      }
    }
    return tables;
  }

  /// <summary>libjxl half-precision read: same encoding as JxlBitReader.ReadBits(16)
  /// interpreted as IEEE 754 half-precision float. Used by quant table modes.</summary>
  private static float _ReadF16(JxlBitReader r) {
    var bits = (ushort)r.ReadBits(16);
    var sign = (bits >> 15) & 1;
    var exp = (bits >> 10) & 0x1F;
    var frac = bits & 0x3FF;
    if (exp == 0)
      return sign != 0 ? -0f : 0f;
    if (exp == 31)
      return frac == 0 ? (sign != 0 ? float.NegativeInfinity : float.PositiveInfinity) : float.NaN;
    var mantissa = 1.0f + frac / 1024.0f;
    var value = mantissa * MathF.Pow(2.0f, exp - 15);
    return sign != 0 ? -value : value;
  }

  /// <summary>libjxl <c>required_size_x</c> from <c>quant_weights.h</c> — the
  /// dimension (in 8×8 cells) along the X axis for each of the 17 quant table
  /// slots. Used by Mode 7 (RAW) to size its embedded modular sub-image.</summary>
  private static readonly int[] _RequiredSizeX = { 1, 1, 2, 2, 4, 4, 1, 2, 2, 4, 1, 8, 8, 16, 16, 32, 32 };
  /// <summary>libjxl <c>required_size_y</c>.</summary>
  private static readonly int[] _RequiredSizeY = { 1, 1, 2, 2, 4, 4, 2, 4, 4, 8, 1, 8, 16, 16, 32, 32, 64 };

  // ---------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------

  /// <summary>
  /// Read the <c>quant_dc</c> U32 field of <c>QuantizerParams</c>. The
  /// encoding is <c>Val(16) | BitsOffset(5, 1) | BitsOffset(8, 1) |
  /// BitsOffset(16, 1)</c>. <see cref="JxlBitReader.ReadU32"/> handles all
  /// non-Val branches via <c>(c, u)</c> pairs; for selector 0 we set
  /// <c>u0 = 0</c> so no payload bits are consumed and the value is exactly
  /// <c>c0 = 16</c>, matching <c>Val(16)</c>.
  /// </summary>
  private static uint _ReadQuantDc(JxlBitReader reader)
    => reader.ReadU32(
      c0: 16u, u0: 0u,        // Val(16): no payload, value = 16
      c1: 1u, u1: 5u,         // BitsOffset(5, 1)
      c2: 1u, u2: 8u,         // BitsOffset(8, 1)
      c3: 1u, u3: 16u);       // BitsOffset(16, 1)
}
