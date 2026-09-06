using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261;

/// <summary>
/// The write direction of the block layer of ITU-T H.261 clause 4.2.4: sixty-four samples in,
/// run-level codes out, and the very samples the decoder will rebuild from them back again.
/// </summary>
/// <remarks>
/// Quantisation is the encoder's own choice and the Recommendation states none — only the
/// reconstruction the decoder performs, which is <see cref="H263Quantisation.Dequantise"/>. What is
/// used here is the rule the ITU's own test model states, <c>|LEVEL| = |COF| / (2 &#215; QUANT)</c>
/// truncated towards zero, whose one property worth naming is that it never overshoots: the
/// reconstruction of a truncated level is never further from zero than the coefficient it came from,
/// so a residual coded this way cannot amplify itself through a run of predicted pictures.
/// <para/>
/// <b>Reconstruction is done here rather than left to the decoder.</b> Every picture is coded against
/// what the last one <i>reconstructed to</i> and not against what was handed in, so the samples this
/// class hands back are the encoder's copy of the decoder's picture buffer. They are built by the same
/// two calls the decoder makes — <see cref="H263Quantisation"/> then <see cref="H263InverseDct"/> — so
/// what drifts between an encoder and a decoder here is only what Annex A's accuracy bound already
/// allows two conforming inverse transforms to differ by, and never a difference in what was coded.
/// </remarks>
internal static class H261BlockEncoder {

  /// <summary>The largest magnitude Table 5's eight-bit escape level can carry (-128 is FORBIDDEN).</summary>
  private const int _MaxLevel = 127;

  /// <summary>
  /// Quantises an intra block, answering the eight-bit INTRADC field of 4.2.4.2 and filling
  /// <paramref name="levels"/> with its alternating-current levels in raster order.
  /// </summary>
  /// <remarks>
  /// The field is a value and not a level: clause 4.2.4.2 fixes its step at eight regardless of QUANT,
  /// leaves 0 and 128 unused, and has 255 stand in for the reconstruction level 1024 that 128 would
  /// otherwise have carried. So the rounded quotient is clamped into 1 to 254 and the one value that
  /// lands on 128 is written as 255 — a picture of flat white codes to 254 rather than to 255, because
  /// writing 255 would state a mid-grey block.
  /// </remarks>
  internal static int QuantiseIntra(scoped ReadOnlySpan<int> samples, int quantiser, scoped Span<int> levels) {
    Span<double> coefficients = stackalloc double[64];
    H261ForwardDct.Transform(samples, coefficients);

    levels.Clear();
    for (var position = 1; position < 64; ++position) {
      var raster = H263Quantisation.ZigZag[position];
      levels[raster] = _Level(coefficients[raster], quantiser);
    }

    var direct = (int)Math.Round(coefficients[0] / 8d, MidpointRounding.AwayFromZero);
    var clamped = direct < 1 ? 1 : direct > 254 ? 254 : direct;

    return clamped == 128 ? 255 : clamped;
  }

  /// <summary>
  /// Quantises a prediction residual, answering whether anything survived it.
  /// </summary>
  /// <remarks>
  /// A block whose levels are all zero is one the coded block pattern leaves out entirely, so this
  /// question is what decides the pattern of clause 4.2.3.3 rather than anything read off the samples.
  /// </remarks>
  internal static bool QuantiseInter(scoped ReadOnlySpan<int> residual, int quantiser, scoped Span<int> levels) {
    Span<double> coefficients = stackalloc double[64];
    H261ForwardDct.Transform(residual, coefficients);

    levels.Clear();
    var coded = false;
    for (var position = 0; position < 64; ++position) {
      var raster = H263Quantisation.ZigZag[position];
      var level = _Level(coefficients[raster], quantiser);
      levels[raster] = level;
      coded |= level != 0;
    }

    return coded;
  }

  /// <summary>
  /// Rebuilds the sixty-four samples a decoder will produce from these levels, by the decoder's own
  /// two steps.
  /// </summary>
  /// <param name="levels">The quantised levels in raster order.</param>
  /// <param name="quantiser">QUANT, 1 to 31.</param>
  /// <param name="intraDirect">The INTRADC field for an intra block, or zero for a residual.</param>
  /// <param name="block">Filled with the reconstructed samples in raster order.</param>
  internal static void Reconstruct(
    scoped ReadOnlySpan<int> levels, int quantiser, int intraDirect, scoped Span<int> block) {
    block.Clear();

    if (intraDirect != 0)
      block[0] = H263Quantisation.DequantiseIntraDc(intraDirect);

    for (var position = intraDirect != 0 ? 1 : 0; position < 64; ++position) {
      var raster = H263Quantisation.ZigZag[position];
      var level = levels[raster];
      if (level != 0)
        block[raster] = H263Quantisation.Dequantise(level, quantiser);
    }

    H263InverseDct.Transform(block);
  }

  /// <summary>Writes an intra block: the eight-bit INTRADC field, then its alternating-current levels.</summary>
  /// <remarks>
  /// Every one of those levels comes from <see cref="H261VlcTables.CoefficientNotFirst"/>, including
  /// the very first — an intra block's "first coefficient" in Table 5's sense is its first alternating
  /// current term only in the decoder's scan-position bookkeeping, and the footnote's shorter spelling
  /// of run 0, level 1 belongs to a block whose first transmitted symbol is a coefficient rather than
  /// one whose direct current arrived in a fixed-length field of its own.
  /// </remarks>
  internal static void WriteIntra(H261BitWriter writer, int intraDirect, scoped ReadOnlySpan<int> levels) {
    writer.Write(intraDirect, 8);
    _WriteCoefficients(writer, levels, from: 1, position: 0, first: false);
  }

  /// <summary>Writes a coded residual block, whose first symbol alone is spelled by Table 5's footnote.</summary>
  internal static void WriteInter(H261BitWriter writer, scoped ReadOnlySpan<int> levels)
    => _WriteCoefficients(writer, levels, from: 0, position: -1, first: true);

  /// <summary>
  /// The truncating quantiser of the test model, saturated to what Table 5's escape can state.
  /// </summary>
  private static int _Level(double coefficient, int quantiser) {
    var level = (int)(coefficient / (2d * quantiser));
    return level < -_MaxLevel ? -_MaxLevel : level > _MaxLevel ? _MaxLevel : level;
  }

  private static void _WriteCoefficients(
    H261BitWriter writer, scoped ReadOnlySpan<int> levels, int from, int position, bool first) {
    for (var scan = from; scan < 64; ++scan) {
      var level = levels[H263Quantisation.ZigZag[scan]];
      if (level == 0)
        continue;

      var table = first ? H261VlcWriter.CoefficientFirst : H261VlcWriter.CoefficientNotFirst;
      first = false;

      var run = scan - position - 1;
      position = scan;

      var magnitude = level < 0 ? -level : level;
      if (H261VlcTables.TryIndexOf(run, magnitude, out var value)) {
        writer.WriteCode(table[value]);
        writer.WriteBit(level < 0 ? 1 : 0);
        continue;
      }

      // The escape of 4.2.4.1: a six-bit run and an eight-bit two's-complement level, and no sign bit
      // of its own — the level carries its own sign.
      writer.WriteCode(table[H261VlcTables.CoefficientEscape]);
      writer.Write(run, 6);
      writer.Write(level & 0xFF, 8);
    }

    writer.WriteCode(H261VlcWriter.CoefficientNotFirst[H261VlcTables.CoefficientEob]);
  }
}
