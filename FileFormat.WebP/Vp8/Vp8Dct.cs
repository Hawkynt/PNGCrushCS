namespace FileFormat.WebP.Vp8;

/// <summary>Integer DCT/IDCT for VP8: simplified 4x4 inverse transform and Walsh-Hadamard transform for DC coefficients.</summary>
internal static class Vp8Dct {

  // VP8 fixed-point multiplier constants (from the spec):
  // cos(pi/8) * sqrt(2) ~= 1.08239 -> 20091/65536 fractional part
  // sin(pi/8) * sqrt(2) ~= 0.54120 -> 35468/65536 fractional part
  private const int _COS_FRAC = 20091;
  private const int _SIN_FRAC = 35468;

  /// <summary>4x4 inverse DCT: adds result to the destination block.</summary>
  public static void InverseDct4x4(short[] coeffs, byte[] dst, int dstOffset, int dstStride) {
    var tmp = new int[16];

    // Horizontal pass (rows)
    for (var i = 0; i < 4; ++i) {
      var c0 = coeffs[i * 4 + 0];
      var c1 = coeffs[i * 4 + 1];
      var c2 = coeffs[i * 4 + 2];
      var c3 = coeffs[i * 4 + 3];

      var a1 = c0 + c2;
      var b1 = c0 - c2;
      var t1 = (c1 * _SIN_FRAC >> 16) - c3 - (c3 * _COS_FRAC >> 16);
      var t2 = c1 + (c1 * _COS_FRAC >> 16) + (c3 * _SIN_FRAC >> 16);

      tmp[i * 4 + 0] = a1 + t2;
      tmp[i * 4 + 1] = b1 + t1;
      tmp[i * 4 + 2] = b1 - t1;
      tmp[i * 4 + 3] = a1 - t2;
    }

    // Vertical pass (columns) + add to dst with rounding
    for (var i = 0; i < 4; ++i) {
      var r0 = tmp[0 * 4 + i];
      var r1 = tmp[1 * 4 + i];
      var r2 = tmp[2 * 4 + i];
      var r3 = tmp[3 * 4 + i];

      var a1 = r0 + r2;
      var b1 = r0 - r2;
      var t1 = (r1 * _SIN_FRAC >> 16) - r3 - (r3 * _COS_FRAC >> 16);
      var t2 = r1 + (r1 * _COS_FRAC >> 16) + (r3 * _SIN_FRAC >> 16);

      var off = dstOffset + i;
      dst[off + 0 * dstStride] = _Clamp(dst[off + 0 * dstStride] + ((a1 + t2 + 4) >> 3));
      dst[off + 1 * dstStride] = _Clamp(dst[off + 1 * dstStride] + ((b1 + t1 + 4) >> 3));
      dst[off + 2 * dstStride] = _Clamp(dst[off + 2 * dstStride] + ((b1 - t1 + 4) >> 3));
      dst[off + 3 * dstStride] = _Clamp(dst[off + 3 * dstStride] + ((a1 - t2 + 4) >> 3));
    }
  }

  /// <summary>4x4 inverse Walsh-Hadamard transform for Y2 (second-order DC) coefficients.</summary>
  public static void InverseWht(short[] coeffs, short[] output) {
    var tmp = new int[16];

    // Horizontal pass
    for (var i = 0; i < 4; ++i) {
      var a0 = coeffs[i * 4 + 0] + coeffs[i * 4 + 2];
      var a1 = coeffs[i * 4 + 0] - coeffs[i * 4 + 2];
      var a2 = coeffs[i * 4 + 1] - coeffs[i * 4 + 3];
      var a3 = coeffs[i * 4 + 1] + coeffs[i * 4 + 3];
      tmp[i * 4 + 0] = a0 + a3;
      tmp[i * 4 + 1] = a1 + a2;
      tmp[i * 4 + 2] = a1 - a2;
      tmp[i * 4 + 3] = a0 - a3;
    }

    // Vertical pass with rounding
    for (var i = 0; i < 4; ++i) {
      var a0 = tmp[0 * 4 + i] + tmp[2 * 4 + i];
      var a1 = tmp[0 * 4 + i] - tmp[2 * 4 + i];
      var a2 = tmp[1 * 4 + i] - tmp[3 * 4 + i];
      var a3 = tmp[1 * 4 + i] + tmp[3 * 4 + i];
      output[i + 0 * 4] = (short)((a0 + a3 + 3) >> 3);
      output[i + 1 * 4] = (short)((a1 + a2 + 3) >> 3);
      output[i + 2 * 4] = (short)((a1 - a2 + 3) >> 3);
      output[i + 3 * 4] = (short)((a0 - a3 + 3) >> 3);
    }
  }

  /// <summary>4x4 inverse DCT for a block with only a single DC coefficient (short-circuit).</summary>
  public static void InverseDct4x4Dc(short dcCoeff, byte[] dst, int dstOffset, int dstStride) {
    var dc = (dcCoeff + 4) >> 3;
    for (var row = 0; row < 4; ++row) {
      var off = dstOffset + row * dstStride;
      for (var col = 0; col < 4; ++col)
        dst[off + col] = _Clamp(dst[off + col] + dc);
    }
  }

  private static byte _Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}
