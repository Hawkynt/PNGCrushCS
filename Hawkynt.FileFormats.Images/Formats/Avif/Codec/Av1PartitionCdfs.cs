using System;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Mutable AV1 partition CDF state. Each tile starts from the section 9.4 default tables and adapts
/// its rows independently while the arithmetic decoder consumes partition symbols.
/// </summary>
internal sealed class Av1PartitionCdfs {

  private static readonly ushort[][] _DEFAULT_W8 = [
    [19132, 25510, 30392, 32768, 0],
    [13928, 19855, 28540, 32768, 0],
    [12522, 23679, 28629, 32768, 0],
    [9896, 18783, 25853, 32768, 0],
  ];

  private static readonly ushort[][] _DEFAULT_W16 = [
    [15597, 20929, 24571, 26706, 27664, 28821, 29601, 30571, 31902, 32768, 0],
    [7925, 11043, 16785, 22470, 23971, 25043, 26651, 28701, 29834, 32768, 0],
    [5414, 13269, 15111, 20488, 22360, 24500, 25537, 26336, 32117, 32768, 0],
    [2662, 6362, 8614, 20860, 23053, 24778, 26436, 27829, 31171, 32768, 0],
  ];

  private static readonly ushort[][] _DEFAULT_W32 = [
    [18462, 20920, 23124, 27647, 28227, 29049, 29519, 30178, 31544, 32768, 0],
    [7689, 9060, 12056, 24992, 25660, 26182, 26951, 28041, 29052, 32768, 0],
    [6015, 9009, 10062, 24544, 25409, 26545, 27071, 27526, 32047, 32768, 0],
    [1394, 2208, 2796, 28614, 29061, 29466, 29840, 30185, 31899, 32768, 0],
  ];

  private static readonly ushort[][] _DEFAULT_W64 = [
    [20137, 21547, 23078, 29566, 29837, 30261, 30524, 30892, 31724, 32768, 0],
    [6732, 7490, 9497, 27944, 28250, 28515, 28969, 29630, 30104, 32768, 0],
    [5945, 7663, 8348, 28683, 29117, 29749, 30064, 30298, 32238, 32768, 0],
    [870, 1212, 1487, 31198, 31394, 31574, 31743, 31881, 32332, 32768, 0],
  ];

  private static readonly ushort[][] _DEFAULT_W128 = [
    [27899, 28219, 28529, 32484, 32539, 32619, 32639, 32768, 0],
    [6607, 6990, 8268, 32060, 32219, 32338, 32371, 32768, 0],
    [5429, 6676, 7122, 32027, 32227, 32531, 32582, 32768, 0],
    [711, 966, 1172, 32448, 32538, 32617, 32664, 32768, 0],
  ];

  private readonly ushort[][] _w8 = _Clone(_DEFAULT_W8);
  private readonly ushort[][] _w16 = _Clone(_DEFAULT_W16);
  private readonly ushort[][] _w32 = _Clone(_DEFAULT_W32);
  private readonly ushort[][] _w64 = _Clone(_DEFAULT_W64);
  private readonly ushort[][] _w128 = _Clone(_DEFAULT_W128);

  /// <summary>Returns the live CDF row for a square block whose size is expressed as log2(pixels).</summary>
  public ushort[] GetPartitionCdf(int blockSizeLog2, int context) {
    if ((uint)context >= 4)
      throw new ArgumentOutOfRangeException(nameof(context));

    return blockSizeLog2 switch {
      3 => _w8[context],
      4 => _w16[context],
      5 => _w32[context],
      6 => _w64[context],
      7 => _w128[context],
      _ => throw new ArgumentOutOfRangeException(nameof(blockSizeLog2), "AV1 partition CDFs exist for 8x8 through 128x128 blocks."),
    };
  }

  /// <summary>Derives the binary split-or-horizontal edge CDF from a full partition CDF.</summary>
  public static ushort[] BuildSplitOrHorizontalCdf(ushort[] partitionCdf, bool block128) {
    ArgumentNullException.ThrowIfNull(partitionCdf);
    var expectedSymbols = block128 ? 8 : 10;
    _ValidatePartitionCdf(partitionCdf, expectedSymbols);

    var splitProbability = _Probability(partitionCdf, (int)Av1PartitionType.Vertical)
      + _Probability(partitionCdf, (int)Av1PartitionType.Split)
      + _Probability(partitionCdf, (int)Av1PartitionType.HorizontalA)
      + _Probability(partitionCdf, (int)Av1PartitionType.VerticalA)
      + _Probability(partitionCdf, (int)Av1PartitionType.VerticalB);
    if (!block128)
      splitProbability += _Probability(partitionCdf, (int)Av1PartitionType.Vertical4);

    return [(ushort)(32768 - splitProbability), 32768, 0];
  }

  /// <summary>Derives the binary split-or-vertical edge CDF from a full partition CDF.</summary>
  public static ushort[] BuildSplitOrVerticalCdf(ushort[] partitionCdf, bool block128) {
    ArgumentNullException.ThrowIfNull(partitionCdf);
    var expectedSymbols = block128 ? 8 : 10;
    _ValidatePartitionCdf(partitionCdf, expectedSymbols);

    var splitProbability = _Probability(partitionCdf, (int)Av1PartitionType.Horizontal)
      + _Probability(partitionCdf, (int)Av1PartitionType.Split)
      + _Probability(partitionCdf, (int)Av1PartitionType.HorizontalA)
      + _Probability(partitionCdf, (int)Av1PartitionType.HorizontalB)
      + _Probability(partitionCdf, (int)Av1PartitionType.VerticalA);
    if (!block128)
      splitProbability += _Probability(partitionCdf, (int)Av1PartitionType.Horizontal4);

    return [(ushort)(32768 - splitProbability), 32768, 0];
  }

  private static int _Probability(ushort[] cdf, int symbol) =>
    cdf[symbol] - (symbol == 0 ? 0 : cdf[symbol - 1]);

  private static void _ValidatePartitionCdf(ushort[] cdf, int symbols) {
    if (cdf.Length < symbols + 1 || cdf[symbols - 1] != 32768)
      throw new ArgumentException("Invalid AV1 partition CDF.", nameof(cdf));
  }

  private static ushort[][] _Clone(ushort[][] source) {
    var result = new ushort[source.Length][];
    for (var i = 0; i < source.Length; ++i)
      result[i] = (ushort[])source[i].Clone();
    return result;
  }
}
