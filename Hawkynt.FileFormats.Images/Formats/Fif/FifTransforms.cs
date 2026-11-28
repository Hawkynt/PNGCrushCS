using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Fif;

/// <summary>One range block's transform in an FTC stream.</summary>
/// <param name="Kind">The class: 0 single, 1 shared by 2×2 blocks, 2 shared by 4×4 blocks, 4–6 the same three drawing on an FTT template.</param>
/// <param name="QuantizationIndex">The raw quantizer index as coded.</param>
/// <param name="Quantized">The affine-table offset the index selects.</param>
/// <param name="DomainX">Domain origin in full-resolution pixels (template pixels times two for kinds 4–6).</param>
/// <param name="DomainY">See <paramref name="DomainX"/>.</param>
/// <param name="Symmetry">One of the eight square symmetries.</param>
/// <param name="Slope">The affine ramp step, which is the quantizer's type number.</param>
/// <param name="QuantizerSelector">Which of the stream's quantizers coded it.</param>
internal readonly record struct FifRecord(
  int Kind, int QuantizationIndex, int Quantized, int DomainX, int DomainY, int Symmetry, int Slope = 6, int QuantizerSelector = 0);

/// <summary>A brightness quantizer: a type, a width, and the affine-table offsets it produces.</summary>
internal sealed record FifQuantizer(int Kind, int Bits, int Step, int Base, int[] Values);

/// <summary>Tables and geometry shared by every FTC variant.</summary>
/// <remarks>
/// Everything numeric here was measured against Iterated Systems' DECO_32.DLL 6.1: the
/// quantizer ramps, the affine table (a contrast of type/8 applied through a saturating table), the
/// 4×4 tile traversal the masks run in, and the square symmetries.
/// </remarks>
internal static class FifTransforms {

  /// <summary>Within each 4×4 tile of range blocks, the order records are coded in.</summary>
  public static readonly int[] YInTile = [0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 3, 3, 2, 2, 3, 3];
  public static readonly int[] XInTile = [0, 1, 0, 1, 2, 3, 2, 3, 0, 1, 0, 1, 2, 3, 2, 3];

  private static readonly int[] _symmetryDy = [0, 0, 0, 0, 1, 1, -1, -1];
  private static readonly int[] _symmetryDx = [1, -1, 1, -1, 0, 0, 0, 0];
  private static readonly int[] _symmetryRowX = [0, 0, 0, 0, 1, -1, 1, -1];
  private static readonly int[] _symmetryColumnY = [1, 1, -1, -1, 0, 0, 0, 0];
  private static readonly int[] _startX4 = [0, 3, 0, 3, 0, 3, 0, 3];
  private static readonly int[] _startY4 = [0, 0, 3, 3, 0, 0, 3, 3];
  private static readonly int[] _startX2 = [0, 1, 0, 1, 0, 1, 0, 1];
  private static readonly int[] _startY2 = [0, 0, 1, 1, 0, 0, 1, 1];

  /// <summary>The fixed FTC 1.1 profile's 128-entry brightness quantizer.</summary>
  public static readonly int[] FixedQuantization = _BuildFixedQuantization();

  /// <summary>C signed division, truncating toward zero.</summary>
  public static int CDiv(int numerator, int denominator) => numerator / denominator;

  private static int[] _BuildFixedQuantization() {
    var values = new int[128];
    var current = -2048 << 4;
    for (var i = 0; i < values.Length; ++i) {
      var adjusted = Math.Max(current, -0x6000);
      adjusted = adjusted < 0 ? adjusted - 5 : adjusted + 6;
      values[i] = CDiv(adjusted, 12) + 0x800;
      current += 32 << 4;
    }

    return values;
  }

  /// <summary>The affine table: 2048 zeros, then a ramp rising by <paramref name="slope"/>/64 from 0.5.</summary>
  public static int Affine(int quantized, int value, int slope) {
    var index = quantized + value * 8;
    if ((uint)index >= 8192)
      throw new InvalidDataException("FTC affine index is outside its table.");

    return index < 2048 ? 0 : Math.Min((32 + (index - 2048) * slope) >> 6, 255);
  }

  /// <summary>The DLL's ten-bit clamp: 0..255 as is, 256..639 saturate high, the rest are negatives.</summary>
  public static int Clamp10(int value) {
    var index = value & 0x3FF;
    return index < 256 ? index : index < 640 ? 255 : 0;
  }

  /// <summary>Where sample (<paramref name="row"/>, <paramref name="column"/>) of an <paramref name="extent"/>-wide square comes from.</summary>
  public static (int Row, int Column) Transform(int symmetry, int row, int column, int extent) => symmetry switch {
    0 => (row, column),
    1 => (row, extent - 1 - column),
    2 => (extent - 1 - row, column),
    3 => (extent - 1 - row, extent - 1 - column),
    4 => (column, row),
    5 => (column, extent - 1 - row),
    6 => (extent - 1 - column, row),
    _ => (extent - 1 - column, extent - 1 - row),
  };

  /// <summary>The coding order of a block grid padded up to whole 4×4 tiles; -1 marks padding.</summary>
  public static (int[] Order, int PaddedWidth, int PaddedHeight) TileOrder(int widthCells, int heightCells) {
    var paddedWidth = (widthCells + 3) / 4 * 4;
    var paddedHeight = (heightCells + 3) / 4 * 4;
    var order = new int[paddedWidth * paddedHeight];
    var n = 0;
    for (var tileY = 0; tileY < paddedHeight; tileY += 4)
    for (var tileX = 0; tileX < paddedWidth; tileX += 4)
    for (var i = 0; i < 16; ++i) {
      var y = tileY + YInTile[i];
      var x = tileX + XInTile[i];
      order[n++] = y < heightCells && x < widthCells ? y * widthCells + x : -1;
    }

    return (order, paddedWidth, paddedHeight);
  }

  /// <summary>One mask: alternating runs over <paramref name="candidates"/>; state 0 gets <paramref name="mark"/>, state 1 survives.</summary>
  public static List<int> MarkRuns(FifBitReader bits, List<int> candidates, byte[] labels, int mark, string what) {
    var state = bits.Read(1);
    var cursor = 0;
    var remaining = candidates.Count;
    var survivors = new List<int>();
    while (remaining > 0) {
      var run = bits.ReadRun();
      if (run > remaining)
        throw new InvalidDataException($"FTC {what} run of {run} exceeds the {remaining} entries left.");

      for (var i = cursor; i < cursor + run; ++i)
        if (state == 0)
          labels[candidates[i]] |= (byte)mark;
        else
          survivors.Add(candidates[i]);

      cursor += run;
      remaining -= run;
      state ^= 1;
    }

    return survivors;
  }

  /// <summary>The last mask, which labels both of its states.</summary>
  public static void MarkFinalRuns(FifBitReader bits, List<int> candidates, byte[] labels, int zeroLabel, int oneLabel, string what) {
    var state = bits.Read(1);
    var cursor = 0;
    var remaining = candidates.Count;
    while (remaining > 0) {
      var run = bits.ReadRun();
      if (run > remaining)
        throw new InvalidDataException($"FTC {what} run of {run} exceeds the {remaining} entries left.");

      var label = state == 0 ? zeroLabel : oneLabel;
      if (label != 0)
        for (var i = cursor; i < cursor + run; ++i)
          labels[candidates[i]] |= (byte)label;

      cursor += run;
      remaining -= run;
      state ^= 1;
    }
  }

  /// <summary>A grouped record for one member of its 2×2 or 4×4 group: the domain moves with the member, turned by the symmetry.</summary>
  public static FifRecord Grouped(FifRecord baseRecord, int ordinal, int groupSize) {
    var inGroup = ordinal & (groupSize * groupSize - 1);
    var row = YInTile[inGroup];
    var column = XInTile[inGroup];
    var symmetry = baseRecord.Symmetry;
    var startX = groupSize == 4 ? _startX4[symmetry] : _startX2[symmetry];
    var startY = groupSize == 4 ? _startY4[symmetry] : _startY2[symmetry];
    var dx = (startX + _symmetryRowX[symmetry] * row + _symmetryDx[symmetry] * column) * 8;
    var dy = (startY + _symmetryColumnY[symmetry] * row + _symmetryDy[symmetry] * column) * 8;
    return baseRecord with { DomainX = baseRecord.DomainX + dx, DomainY = baseRecord.DomainY + dy };
  }

  /// <summary>Bits needed to code a value below <paramref name="count"/>.</summary>
  public static int BitLength(int count) {
    var bits = 0;
    for (var v = count - 1; v > 0; v >>= 1)
      ++bits;

    return bits;
  }

  /// <summary>An affine quantizer's offsets.</summary>
  public static int[] QuantizerValues(int kind, int bits, int step, int @base, int version) {
    int current, increment;
    if (version < 32) {
      current = @base * 8 - 2048;
      increment = step * 8;
    } else {
      current = @base - 2048;
      increment = step;
    }

    var values = new int[1 << bits];
    switch (kind) {
      case 4:
        current *= 2;
        increment *= 2;
        for (var i = 0; i < values.Length; ++i, current += increment)
          values[i] = Math.Max(current, -2048) + 2048;
        break;
      case 5 or 6 or 7: {
        var denominator = kind switch { 5 => 10, 6 => 12, _ => 14 };
        var lower = kind switch { 5 => -0x5000, 6 => -0x6000, _ => -0x7000 };
        var negativeRound = kind - 1;
        var positiveRound = kind;
        current <<= 4;
        increment <<= 4;
        for (var i = 0; i < values.Length; ++i, current += increment) {
          var adjusted = Math.Max(current, lower);
          adjusted += adjusted >= 0 ? positiveRound : -negativeRound;
          values[i] = CDiv(adjusted, denominator) + 2048;
        }

        break;
      }
      default:
        for (var i = 0; i < values.Length; ++i, current += increment)
          values[i] = Math.Max(current, -2048) + 2048;
        break;
    }

    return values;
  }
}
