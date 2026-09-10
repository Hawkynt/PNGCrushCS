using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H263;

/// <summary>Writes the baseline H.263 block syntax RealVideo 1 reuses.</summary>
internal static class H263BlockEncoder {

  private const int _MAX_LEVEL = 127;
  private static readonly H263CodeTable _Coefficient = new(H263VlcTables.Coefficient);
  private static readonly Dictionary<(bool Last, int Run, int Level), int> _CoefficientRows = _BuildCoefficientRows();

  /// <summary>Quantises an intra block and answers whether any AC coefficient survived.</summary>
  internal static bool QuantiseIntra(
    scoped ReadOnlySpan<int> samples, int quantiser, scoped Span<int> levels, out int intraDirect) {
    Span<double> coefficients = stackalloc double[64];
    H263ForwardDct.Transform(samples, coefficients);

    levels.Clear();
    var coded = false;
    for (var position = 1; position < 64; ++position) {
      var raster = H263Quantisation.ZigZag[position];
      var level = _Level(coefficients[raster], quantiser);
      levels[raster] = level;
      coded |= level != 0;
    }

    var direct = (int)Math.Round(coefficients[0] / 8d, MidpointRounding.AwayFromZero);
    var clamped = direct < 1 ? 1 : direct > 254 ? 254 : direct;
    intraDirect = clamped == 128 ? 255 : clamped;
    return coded;
  }

  /// <summary>Writes the fixed-width intra DC value followed by the coded AC coefficients, if any.</summary>
  internal static void WriteIntra(
    H263BitWriter writer, int intraDirect, scoped ReadOnlySpan<int> levels, bool hasCoefficients) {
    writer.Write(intraDirect, 8);
    if (hasCoefficients)
      _WriteCoefficients(writer, levels);
  }

  /// <summary>Chooses the level whose clause 6.2.1 reconstruction is nearest to the coefficient.</summary>
  private static int _Level(double coefficient, int quantiser) {
    var magnitude = Math.Abs(coefficient);
    var reconstructionOffset = quantiser - ((quantiser & 1) == 0 ? 1 : 0);
    var firstLevel = reconstructionOffset + 2 * quantiser;
    if (2d * magnitude < firstLevel)
      return 0;

    var level = Math.Max(
      1,
      (int)Math.Round((magnitude - reconstructionOffset) / (2d * quantiser), MidpointRounding.AwayFromZero));
    level = Math.Min(level, _MAX_LEVEL);
    return coefficient < 0 ? -level : level;
  }

  private static void _WriteCoefficients(H263BitWriter writer, scoped ReadOnlySpan<int> levels) {
    var lastScan = 63;
    while (lastScan > 0 && levels[H263Quantisation.ZigZag[lastScan]] == 0)
      --lastScan;

    var previous = 0;
    for (var scan = 1; scan <= lastScan; ++scan) {
      var level = levels[H263Quantisation.ZigZag[scan]];
      if (level == 0)
        continue;

      var last = scan == lastScan;
      var run = scan - previous - 1;
      previous = scan;
      var magnitude = Math.Abs(level);

      if (_CoefficientRows.TryGetValue((last, run, magnitude), out var value)) {
        writer.WriteCode(_Coefficient[value]);
        writer.WriteBit(level < 0 ? 1 : 0);
        continue;
      }

      writer.WriteCode(_Coefficient[H263VlcTables.CoefficientEscape]);
      writer.WriteBit(last ? 1 : 0);
      writer.Write(run, 6);
      writer.Write(level & 0xFF, 8);
    }
  }

  private static Dictionary<(bool Last, int Run, int Level), int> _BuildCoefficientRows() {
    var result = new Dictionary<(bool Last, int Run, int Level), int>();
    for (var value = 0; value < H263VlcTables.CoefficientIsLast.Length; ++value)
      result[(
        H263VlcTables.CoefficientIsLast[value],
        H263VlcTables.CoefficientRun[value],
        H263VlcTables.CoefficientLevel[value])] = value;

    return result;
  }
}
