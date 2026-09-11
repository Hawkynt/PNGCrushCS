using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Codecs.H261;

namespace FileFormat.Codecs.Mpeg;

/// <summary>Codes one MPEG non-intra block: the residual left after motion compensation.</summary>
/// <remarks>
/// One writer serves both standards, for the same reason the decoder reads both with one block
/// reader: a non-intra block is the same walk in either, and the two places it differs are worth a
/// flag rather than a second copy that can drift from this one.
/// <para/>
/// A non-intra block has no DC predictor and no separate DC code — every coefficient runs through
/// the same Table B.14 — and it quantises against the non-intra matrix with the dead zone the
/// standard's inverse implies. ISO/IEC 11172-2 defines that inverse as
/// <c>rec = ((2 * level + sign(level)) * quantiser_scale * weight) / 16</c> and ISO/IEC 13818-2 as
/// the same thing over thirty-two, so the forward step that lands nearest a reconstruction point is
/// <c>level = 8 * coefficient / (scale * weight)</c> in MPEG-1 and twice that numerator in MPEG-2,
/// truncated towards zero: rounding away from zero here would systematically overshoot, because the
/// reconstruction adds the sign term back.
/// <para/>
/// The other difference is the escape. MPEG-1 spells a level outside the table as eight bits, with
/// two further forms for a level eight bits cannot hold; MPEG-2 withdrew those and states twelve
/// bits flat.
/// <para/>
/// Quantising is separated from writing because <c>coded_block_pattern</c> has to be written before
/// any block is: the pattern says which of the six blocks carry coefficients at all, so every block
/// must be quantised first and only then written.
/// <para/>
/// No table is copied here. The codes are the decoder's Annex B tables reversed into value-to-code
/// maps, so a corrected transcription changes both directions at once.
/// </remarks>
internal static class MpegInterBlockEncoder {

  private const int _MAX_LEVEL = 255;

  private static readonly IReadOnlyDictionary<int, string> _CoefficientCodes =
    MpegVlcTables.Coefficient.Entries.ToDictionary(static entry => entry.Value, static entry => entry.Code);

  /// <summary>
  /// Transforms and quantises one residual block into <paramref name="levels"/>, in scan order.
  /// </summary>
  /// <returns><see langword="true"/> when any coefficient survived, which is what puts the block in
  /// the coded block pattern.</returns>
  internal static bool TryQuantise(
    scoped ReadOnlySpan<int> residual, int quantiserScale, bool isMpeg2, scoped Span<int> levels) {
    Span<double> coefficients = stackalloc double[64];
    H261ForwardDct.Transform(residual, coefficients);

    var coded = false;
    for (var scan = 0; scan < 64; ++scan) {
      var raster = MpegQuantisation.ZigZagScan[scan];
      var weight = MpegQuantisation.DefaultNonIntraMatrix[raster];
      var scaled = (isMpeg2 ? 16d : 8d) * coefficients[raster] / (quantiserScale * weight);

      // Truncation towards zero is the dead zone: a coefficient worth less than one reconstruction
      // step becomes nothing rather than being rounded up into a step it never reached.
      var level = Math.Clamp((int)scaled, -_MAX_LEVEL, _MAX_LEVEL);
      levels[scan] = level;
      coded |= level != 0;
    }

    return coded;
  }

  /// <summary>Writes a block that <see cref="TryQuantise"/> found at least one coefficient in.</summary>
  internal static void Write(MpegBitWriter writer, scoped ReadOnlySpan<int> levels, bool isMpeg2) {
    var previousScan = -1;
    var isFirst = true;

    for (var scan = 0; scan < 64; ++scan) {
      var level = levels[scan];
      if (level == 0)
        continue;

      var run = scan - previousScan - 1;
      previousScan = scan;

      // dct_coeff_first: at the head of a non-intra block a leading one is the whole code and means
      // a level of one, because no End of Block competes with it for that bit. Every other code
      // begins with a zero and is written from the table unchanged.
      if (isFirst && run == 0 && Math.Abs(level) == 1) {
        writer.WriteCode("1");
        writer.WriteBit(level < 0 ? 1 : 0);
        isFirst = false;
        continue;
      }

      isFirst = false;
      _WriteCoefficient(writer, run, level, isMpeg2);
    }

    writer.WriteCode(_CoefficientCodes[MpegVlcTables.EndOfBlock]);
  }

  private static void _WriteCoefficient(MpegBitWriter writer, int run, int level, bool isMpeg2) {
    var packed = (run << 8) | Math.Abs(level);
    if (_CoefficientCodes.TryGetValue(packed, out var code)) {
      writer.WriteCode(code);
      writer.WriteBit(level < 0 ? 1 : 0);
      return;
    }

    writer.WriteCode(_CoefficientCodes[MpegVlcTables.CoefficientEscape]);
    writer.WriteBits(run, 6);

    // 13818-2 2.4.3.7 replaced MPEG-1's eight-bit escape and its two extensions with one twelve-bit
    // field, so a level the table cannot hold is stated once and directly.
    if (isMpeg2) {
      writer.WriteBits((uint)(level & 0xFFF), 12);
      return;
    }

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
}
