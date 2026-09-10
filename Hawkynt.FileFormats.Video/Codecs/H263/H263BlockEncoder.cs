using System;
using FileFormat.Codecs.H261;

namespace FileFormat.Codecs.H263;

/// <summary>
/// The write direction of the baseline H.263 intra block layer: samples in, INTRADC and Table 16
/// run-level codes out.
/// </summary>
/// <remarks>
/// H.263 specifies the inverse transform and reconstruction, not an encoder's forward transform or
/// quantiser decision. The H.261 encoder beside this already implements the matching forward transform
/// and the same main-text reconstruction quantiser this decoder uses, so its quantisation step is
/// reused rather than duplicated. Only the bitstream spelling differs: H.263 carries LAST in every
/// TCOEF code instead of an EOB symbol.
/// </remarks>
internal static class H263BlockEncoder {

  /// <summary>Quantises an intra block and answers whether any alternating-current coefficient remains.</summary>
  internal static bool QuantiseIntra(
    scoped ReadOnlySpan<int> samples, int quantiser, scoped Span<int> levels, out int intraDirect) {
    intraDirect = H261BlockEncoder.QuantiseIntra(samples, quantiser, levels);

    for (var scan = 1; scan < 64; ++scan)
      if (levels[H263Quantisation.ZigZag[scan]] != 0)
        return true;

    return false;
  }

  /// <summary>Writes one intra block, including its fixed-width DC value and any AC coefficients.</summary>
  internal static void WriteIntra(
    H263BitWriter writer, int intraDirect, scoped ReadOnlySpan<int> levels, bool hasCoefficients) {
    writer.Write(intraDirect, 8);
    if (!hasCoefficients)
      return;

    var lastScan = 63;
    while (lastScan > 0 && levels[H263Quantisation.ZigZag[lastScan]] == 0)
      --lastScan;

    var previous = 0;
    for (var scan = 1; scan <= lastScan; ++scan) {
      var level = levels[H263Quantisation.ZigZag[scan]];
      if (level == 0)
        continue;

      var run = scan - previous - 1;
      previous = scan;
      var last = scan == lastScan;
      var magnitude = level < 0 ? -level : level;

      if (H263VlcWriter.TryCoefficient(last, run, magnitude, out var code)) {
        writer.WriteCode(code);
        writer.WriteBit(level < 0 ? 1 : 0);
        continue;
      }

      // Table 16's escape: LAST, six-bit RUN and an eight-bit two's-complement LEVEL. The quantiser
      // used above saturates at ±127, so neither forbidden zero nor Annex T's reserved -128 can occur.
      writer.WriteCode(H263VlcWriter.CoefficientEscape);
      writer.WriteBit(last ? 1 : 0);
      writer.Write(run, 6);
      writer.Write(level & 0xFF, 8);
    }
  }
}
