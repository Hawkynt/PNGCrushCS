using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv2;

/// <summary>
/// Reads one of ASV2's eight-by-eight blocks, clause 4.5: a four-bit coefficient group count, an
/// eight-bit DC coefficient, coefficient group zero's own pattern and coefficients, then that many
/// further coefficient groups and theirs.
/// </summary>
/// <remarks>
/// Where ASV1 finds the end of a block from an explicit End Of Block code, ASV2 states the count of
/// coefficient groups up front instead, so there is nothing here that reads past what the block
/// actually holds and nothing to check for a block reading one group too many.
/// </remarks>
internal static class Asv2BlockDecoder {

  /// <summary>
  /// Reads one block into sixty-four raster-order samples, already dequantised and transformed.
  /// </summary>
  /// <param name="dequantFactors">
  /// <c>floor(128 * q[i] / QP)</c> for every raster position, clause 3.6 — built once for the whole
  /// stream from its single, per-file quantisation parameter.
  /// </param>
  internal static void Read(ref H263BitReader reader, scoped Span<int> block, ReadOnlySpan<int> dequantFactors) {
    block.Clear();

    var count = Asv2Bitstream.ReadReversedBits(ref reader, 4);
    block[0] = Asv2Bitstream.ReadReversedBits(ref reader, 8) * 8; // clause 3.5: c00' = 8 * c00, unconditionally.

    var first = Asv2VlcTables.FirstCoefficientPattern.Read(ref reader);
    _ReadGroup(ref reader, block, dequantFactors, 0, first);

    for (var group = 1; group <= count; ++group) {
      var pattern = Asv2VlcTables.CodedCoefficientPattern.Read(ref reader);
      _ReadGroup(ref reader, block, dequantFactors, group, pattern);
    }

    H263InverseDct.Transform(block);
  }

  private static void _ReadGroup(
    ref H263BitReader reader, scoped Span<int> block, ReadOnlySpan<int> dequantFactors, int group, int pattern) {
    for (var withinGroup = 0; withinGroup < 4; ++withinGroup) {
      if (((pattern >> (3 - withinGroup)) & 1) == 0)
        continue;

      var position = Asv2VlcTables.ScanPosition[group * 4 + withinGroup];
      var level = Asv2VlcTables.ReadLevel(ref reader);
      block[position] = (level * dequantFactors[position]) >> 4; // clause 3.6, floor division by sixteen.
    }
  }
}
