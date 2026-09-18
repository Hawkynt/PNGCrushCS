using System;

namespace FileFormat.Codecs.H264;

/// <summary>Inverse lookups for the specification-defined CAVLC tables shared with the decoder.</summary>
internal static class H264CavlcEncoding {

  private static readonly int[] _InterCodedBlockPattern = [
    0, 16, 1, 2, 4, 8, 32, 3, 5, 10, 12, 15, 47, 7, 11, 13,
    14, 6, 9, 31, 35, 37, 42, 44, 33, 34, 36, 40, 39, 43, 45, 46,
    17, 18, 20, 24, 19, 21, 26, 28, 23, 27, 29, 30, 22, 25, 38, 41,
  ];

  internal readonly record struct Code(uint Bits, int Length);

  internal static Code CoeffToken(int nC, int totalCoeff, int trailingOnes) {
    if ((uint)totalCoeff > 16 || (uint)trailingOnes > 3 || trailingOnes > totalCoeff)
      throw new ArgumentOutOfRangeException(nameof(totalCoeff));

    var table = nC switch {
      -1 => 4,
      < 2 => 0,
      < 4 => 1,
      < 8 => 2,
      _ => 3,
    };
    return _Find(table, (totalCoeff << 2) | trailingOnes);
  }

  internal static Code TotalZeros(int totalCoeff, int totalZeros, bool chromaDc) {
    if (totalCoeff <= 0)
      throw new ArgumentOutOfRangeException(nameof(totalCoeff));
    var table = (chromaDc ? 20 : 5) + totalCoeff - 1;
    return _Find(table, totalZeros);
  }

  internal static Code RunBefore(int zerosLeft, int runBefore) {
    if (zerosLeft <= 0)
      throw new ArgumentOutOfRangeException(nameof(zerosLeft));
    return _Find(23 + Math.Min(zerosLeft, 7) - 1, runBefore);
  }

  internal static int InterCodedBlockPatternCodeNum(int codedBlockPattern) {
    var index = Array.IndexOf(_InterCodedBlockPattern, codedBlockPattern);
    return index >= 0
      ? index
      : throw new ArgumentOutOfRangeException(nameof(codedBlockPattern));
  }

  private static Code _Find(int tableIndex, int value) {
    var currentIndex = 0;
    foreach (var table in H264CavlcTables.AllTables) {
      if (currentIndex++ != tableIndex)
        continue;

      foreach (var (text, candidate) in table.Entries) {
        if (candidate != value)
          continue;

        var bits = 0u;
        var length = 0;
        foreach (var symbol in text) {
          if (symbol is ' ' or '_' or '\t')
            continue;
          bits = (bits << 1) | (symbol == '1' ? 1u : 0u);
          ++length;
        }
        return new(bits, length);
      }

      break;
    }

    throw new InvalidOperationException($"CAVLC table {tableIndex} has no code for value {value}.");
  }
}
