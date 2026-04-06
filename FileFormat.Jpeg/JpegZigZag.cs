namespace FileFormat.Jpeg;

/// <summary>8x8 zigzag scan order tables (ITU-T T.81 Figure A.6).</summary>
internal static class JpegZigZag {

  /// <summary>Maps zigzag index to natural (row-major) index.</summary>
  public static readonly byte[] Order = [
    0, 1, 8, 16, 9, 2, 3, 10,
    17, 24, 32, 25, 18, 11, 4, 5,
    12, 19, 26, 33, 40, 48, 41, 34,
    27, 20, 13, 6, 7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36,
    29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46,
    53, 60, 61, 54, 47, 55, 62, 63
  ];

  /// <summary>Maps natural (row-major) index to zigzag index.</summary>
  public static readonly byte[] Inverse = _BuildInverse();

  private static byte[] _BuildInverse() {
    var inv = new byte[64];
    for (var i = 0; i < 64; ++i)
      inv[Order[i]] = (byte)i;
    return inv;
  }
}
