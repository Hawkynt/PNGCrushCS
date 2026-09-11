using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Codecs.H261;

namespace FileFormat.Codecs.Mpeg;

/// <summary>Codes one MPEG-1 intra block from its sixty-four samples.</summary>
/// <remarks>
/// ISO/IEC 11172-2 fixes the inverse quantiser, not an encoder's transform implementation. The
/// forward transform therefore reuses the same 8x8 orthonormal basis already used by the H.261
/// encoder in this assembly. AC coefficients follow Annex D's test-model quantiser,
/// <c>level = 8 * coefficient / (quantiser_scale * matrix_weight)</c>, rounded to nearest and clipped
/// to the ±255 range the MPEG-1 escape syntax can state. DC has its fixed step of eight.
/// <para/>
/// No table is copied here. The variable-length codes are the decoder's existing Annex B tables,
/// reversed once into value-to-code maps, so a corrected transcription changes both directions.
/// </remarks>
internal static class Mpeg1BlockEncoder {

  private const int _MAX_LEVEL = 255;

  private static readonly IReadOnlyDictionary<int, string> _LuminanceDcCodes = _Reverse(MpegVlcTables.Mpeg1LuminanceDcSize);
  private static readonly IReadOnlyDictionary<int, string> _ChrominanceDcCodes = _Reverse(MpegVlcTables.Mpeg1ChrominanceDcSize);
  private static readonly IReadOnlyDictionary<int, string> _CoefficientCodes = _Reverse(MpegVlcTables.Coefficient);

  /// <summary>Transforms, quantises and writes an intra block, updating its component's DC predictor.</summary>
  internal static void Write(
    MpegBitWriter writer,
    scoped ReadOnlySpan<int> samples,
    bool isChroma,
    int quantiserScale,
    ref int dcPredictor) {
    Span<double> coefficients = stackalloc double[64];
    H261ForwardDct.Transform(samples, coefficients);

    var dc = Math.Clamp((int)Math.Round(coefficients[0] / 8d, MidpointRounding.AwayFromZero), 0, 255);
    _WriteDc(writer, dc - dcPredictor, isChroma);
    dcPredictor = dc;

    var previousScan = 0;
    for (var scan = 1; scan < 64; ++scan) {
      var raster = MpegQuantisation.ZigZagScan[scan];
      var scaled = 8d * coefficients[raster]
        / (quantiserScale * MpegQuantisation.DefaultIntraMatrix[raster]);
      var level = Math.Clamp((int)Math.Round(scaled, MidpointRounding.AwayFromZero), -_MAX_LEVEL, _MAX_LEVEL);
      if (level == 0)
        continue;

      var run = scan - previousScan - 1;
      previousScan = scan;
      _WriteCoefficient(writer, run, level);
    }

    writer.WriteCode(_CoefficientCodes[MpegVlcTables.EndOfBlock]);
  }

  private static void _WriteDc(MpegBitWriter writer, int differential, bool isChroma) {
    var magnitude = Math.Abs(differential);
    var size = magnitude == 0 ? 0 : 32 - int.LeadingZeroCount(magnitude);
    var codes = isChroma ? _ChrominanceDcCodes : _LuminanceDcCodes;

    if (!codes.TryGetValue(size, out var code))
      throw new InvalidOperationException(
        $"An MPEG-1 intra DC differential of {differential} needs size {size}, which Tables B.12/B.13 cannot state.");

    writer.WriteCode(code);
    if (size == 0)
      return;

    var bits = differential > 0 ? differential : differential + (1 << size) - 1;
    writer.WriteBits(bits, size);
  }

  private static void _WriteCoefficient(MpegBitWriter writer, int run, int level) {
    var packed = (run << 8) | Math.Abs(level);
    if (_CoefficientCodes.TryGetValue(packed, out var code)) {
      writer.WriteCode(code);
      writer.WriteBit(level < 0 ? 1 : 0);
      return;
    }

    writer.WriteCode(_CoefficientCodes[MpegVlcTables.CoefficientEscape]);
    writer.WriteBits(run, 6);

    if (level is >= -127 and <= 127) {
      writer.WriteBits(level & 0xFF, 8);
      return;
    }

    if (level > 0) {
      writer.WriteBits(0, 8);
      writer.WriteBits(level, 8);
      return;
    }

    writer.WriteBits(0x80, 8);
    writer.WriteBits(level + 256, 8);
  }

  private static IReadOnlyDictionary<int, string> _Reverse(MpegVlcTable table)
    => table.Entries.ToDictionary(static entry => entry.Value, static entry => entry.Code);
}
