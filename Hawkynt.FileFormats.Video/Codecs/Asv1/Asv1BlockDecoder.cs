using System;
using System.IO;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv1;

/// <summary>
/// Reads one of ASV1's eight-by-eight blocks, clause 4.4: an eight-bit DC coefficient and, group by
/// group, whichever AC coefficients the coefficient group's own pattern code names — reused unchanged
/// from ITU-T H.263 for the inverse transform once dequantisation has produced the sixty-four raster
/// samples ASV1 shares its formula for.
/// </summary>
/// <remarks>
/// A block never carries more than ten coefficient groups (clause 3.3: groups ten to fifteen "cannot
/// be coded"), so the group loop runs at most eleven times — ten real groups and one call that exists
/// only to confirm the block ends in End Of Block rather than run past what a block holds, exactly as
/// the document's own example decoder checks for it.
/// </remarks>
internal static class Asv1BlockDecoder {

  /// <summary>
  /// Reads one block into sixty-four raster-order samples, already dequantised and transformed.
  /// </summary>
  /// <param name="dequantFactors">
  /// <c>floor(64 * q[i] / QP)</c> for every raster position, clause 3.6 — built once for the whole
  /// stream from its single, per-file quantisation parameter.
  /// </param>
  internal static void Read(ref H263BitReader reader, scoped Span<int> block, ReadOnlySpan<int> dequantFactors) {
    block.Clear();
    block[0] = reader.ReadBits(8) * 8; // clause 3.5: c00' = 8 * c00, unconditionally.

    for (var group = 0; ; ++group) {
      var pattern = Asv1VlcTables.CodedCoefficientPattern.Read(ref reader);
      if (pattern == Asv1VlcTables.EndOfBlock)
        break;

      if (group == 10)
        throw new InvalidDataException(
          "An ASV1 block reads a tenth coefficient group without having reached End Of Block first, which "
          + "asv1.txt's own example decoder treats as an error: only groups zero to nine may carry a coefficient.");

      for (var withinGroup = 0; withinGroup < 4; ++withinGroup) {
        if (((pattern >> withinGroup) & 1) == 0)
          continue;

        var position = Asv1VlcTables.ScanPosition[group * 4 + withinGroup];
        if (position == 0)
          throw new InvalidDataException(
            "An ASV1 coefficient group pattern names the block's own DC position, which asv1.txt 3.3 states must "
            + "always be coded as zero and read from the block's separate eight-bit DC field instead.");

        var level = Asv1VlcTables.ReadLevel(ref reader);
        block[position] = (level * dequantFactors[position]) >> 4; // clause 3.6, floor division by sixteen.
      }
    }

    H263InverseDct.Transform(block);
  }
}
