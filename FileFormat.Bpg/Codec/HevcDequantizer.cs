using System;

namespace FileFormat.Bpg.Codec;

/// <summary>Inverse quantization for HEVC residual coefficients with default scaling lists.</summary>
internal static class HevcDequantizer {

  // Level scale table: levelScale[qP % 6]
  private static readonly int[] _LevelScale = [40, 45, 51, 57, 64, 72];

  /// <summary>Performs inverse quantization (scaling) on transform coefficients.</summary>
  /// <param name="coeffs">Input quantized coefficients (modified in place).</param>
  /// <param name="size">Transform block size (4, 8, 16, or 32).</param>
  /// <param name="qp">Quantization parameter for this block.</param>
  /// <param name="bitDepth">Bit depth.</param>
  public static void Dequantize(int[] coeffs, int size, int qp, int bitDepth) {
    var qpPer = qp / 6;
    var qpRem = qp % 6;
    var scale = _LevelScale[qpRem];

    // Shift amount for inverse quantization
    // For HEVC: shift = bitDepth - 9 + log2(size) + qP/6
    var log2Size = _Log2(size);
    var shift = bitDepth - 9 + log2Size;
    var transformShift = Math.Max(0, shift);

    // Scaling with default flat scaling list (all values = 16)
    var flatScaleValue = 16;
    var totalCount = size * size;

    if (qpPer >= transformShift) {
      var leftShift = qpPer - transformShift;
      for (var i = 0; i < totalCount; ++i) {
        var coeff = coeffs[i];
        if (coeff == 0)
          continue;
        coeffs[i] = (coeff * flatScaleValue * scale + (1 << (3))) >> 4 << leftShift;
      }
    } else {
      var rightShift = transformShift - qpPer;
      var addOffset = 1 << (rightShift - 1);
      for (var i = 0; i < totalCount; ++i) {
        var coeff = coeffs[i];
        if (coeff == 0)
          continue;
        coeffs[i] = (coeff * flatScaleValue * scale + addOffset) >> rightShift >> 4;
      }
    }
  }

  /// <summary>Performs inverse quantization with explicit per-coefficient scaling.</summary>
  /// <param name="coeffs">Input quantized coefficients (modified in place).</param>
  /// <param name="scalingList">Per-coefficient scale factors (size*size entries).</param>
  /// <param name="size">Transform block size.</param>
  /// <param name="qp">Quantization parameter.</param>
  /// <param name="bitDepth">Bit depth.</param>
  public static void DequantizeWithScalingList(int[] coeffs, int[] scalingList, int size, int qp, int bitDepth) {
    var qpPer = qp / 6;
    var qpRem = qp % 6;
    var scale = _LevelScale[qpRem];
    var log2Size = _Log2(size);
    var shift = bitDepth - 9 + log2Size;
    var transformShift = Math.Max(0, shift);
    var totalCount = size * size;

    if (qpPer >= transformShift) {
      var leftShift = qpPer - transformShift;
      for (var i = 0; i < totalCount; ++i) {
        var coeff = coeffs[i];
        if (coeff == 0)
          continue;
        var sl = scalingList[i];
        coeffs[i] = (coeff * sl * scale + (1 << 3)) >> 4 << leftShift;
      }
    } else {
      var rightShift = transformShift - qpPer;
      var addOffset = 1 << (rightShift - 1);
      for (var i = 0; i < totalCount; ++i) {
        var coeff = coeffs[i];
        if (coeff == 0)
          continue;
        var sl = scalingList[i];
        coeffs[i] = (coeff * sl * scale + addOffset) >> rightShift >> 4;
      }
    }
  }

  /// <summary>Returns the default flat scaling list for the given transform size.</summary>
  public static int[] GetDefaultScalingList(int size) {
    var list = new int[size * size];
    Array.Fill(list, 16);
    return list;
  }

  /// <summary>Computes the chroma QP from the luma QP using the HEVC chroma QP mapping table.</summary>
  public static int LumaToChromaQp(int lumaQp) {
    if (lumaQp < 0)
      return lumaQp;
    if (lumaQp <= 29)
      return lumaQp;

    return lumaQp switch {
      30 => 29,
      31 => 30,
      32 => 31,
      33 => 32,
      34 => 32,
      35 => 33,
      36 => 34,
      37 => 34,
      38 => 35,
      39 => 35,
      40 => 36,
      41 => 36,
      42 => 37,
      43 => 37,
      _ => Math.Min(lumaQp - 6, 51),
    };
  }

  private static int _Log2(int n) {
    var log = 0;
    var v = n;
    while (v > 1) {
      v >>= 1;
      ++log;
    }
    return log;
  }
}
