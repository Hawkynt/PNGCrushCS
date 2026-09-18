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


  /// <summary>Writes one <c>residual_block_cavlc</c> in scan order.</summary>
  internal static int WriteBlock(
    H264BitWriter writer,
    ReadOnlySpan<int> coeffLevel,
    int nC,
    bool chromaDc) {
    ArgumentNullException.ThrowIfNull(writer);
    if (coeffLevel.Length is <= 0 or > 16)
      throw new ArgumentOutOfRangeException(nameof(coeffLevel));

    Span<int> positions = stackalloc int[16];
    var totalCoeff = 0;
    for (var position = 0; position < coeffLevel.Length; ++position)
      if (coeffLevel[position] != 0)
        positions[totalCoeff++] = position;

    Span<int> levels = stackalloc int[16];
    Span<int> runs = stackalloc int[16];
    for (var i = 0; i < totalCoeff; ++i) {
      var source = totalCoeff - 1 - i;
      var position = positions[source];
      levels[i] = coeffLevel[position];
      runs[i] = source == 0 ? position : position - positions[source - 1] - 1;
    }

    var trailingOnes = 0;
    while (trailingOnes < totalCoeff && trailingOnes < 3 && Math.Abs(levels[trailingOnes]) == 1)
      ++trailingOnes;

    _WriteCode(writer, CoeffToken(nC, totalCoeff, trailingOnes));
    if (totalCoeff == 0)
      return 0;

    for (var i = 0; i < trailingOnes; ++i)
      writer.WriteBit(levels[i] < 0);

    var suffixLength = totalCoeff > 10 && trailingOnes < 3 ? 1 : 0;
    for (var i = trailingOnes; i < totalCoeff; ++i) {
      var level = levels[i];
      var levelCode = level > 0 ? checked(2L * level - 2) : checked(-2L * level - 1);
      if (i == trailingOnes && trailingOnes < 3)
        levelCode -= 2;

      _WriteLevel(writer, levelCode, suffixLength);
      if (suffixLength == 0)
        suffixLength = 1;
      if (Math.Abs(level) > 3 << (suffixLength - 1) && suffixLength < 6)
        ++suffixLength;
    }

    var totalZeros = 0;
    for (var i = 0; i < totalCoeff; ++i)
      totalZeros += runs[i];

    if (totalCoeff < coeffLevel.Length)
      _WriteCode(writer, TotalZeros(totalCoeff, totalZeros, chromaDc));

    var zerosLeft = totalZeros;
    for (var i = 0; i < totalCoeff - 1; ++i) {
      if (zerosLeft == 0)
        break;
      _WriteCode(writer, RunBefore(zerosLeft, runs[i]));
      zerosLeft -= runs[i];
    }

    return totalCoeff;
  }

  private static void _WriteLevel(H264BitWriter writer, long levelCode, int suffixLength) {
    if (levelCode < 0)
      throw new InvalidOperationException("CAVLC level coding reached a negative levelCode.");

    for (var prefix = 0; prefix <= 31; ++prefix) {
      var suffixSize = prefix == 14 && suffixLength == 0 ? 4
        : prefix >= 15 ? prefix - 3
        : suffixLength;
      var baseCode = (long)Math.Min(15, prefix) << suffixLength;
      if (prefix >= 15 && suffixLength == 0)
        baseCode += 15;
      if (prefix >= 16)
        baseCode += (1L << (prefix - 3)) - 4096;

      var suffix = levelCode - baseCode;
      var limit = 1L << suffixSize;
      if (suffix < 0 || suffix >= limit)
        continue;

      for (var i = 0; i < prefix; ++i)
        writer.WriteBit(false);
      writer.WriteBit(true);
      if (suffixSize > 0)
        writer.WriteBits((uint)suffix, suffixSize);
      return;
    }

    throw new InvalidOperationException($"CAVLC cannot represent levelCode {levelCode} with suffixLength {suffixLength}.");
  }

  private static void _WriteCode(H264BitWriter writer, Code code)
    => writer.WriteBits(code.Bits, code.Length);

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
