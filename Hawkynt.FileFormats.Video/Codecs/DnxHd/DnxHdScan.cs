namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// The order a block's coefficients arrive in — SMPTE ST 2019-1:2016, 8.2.6 and Figure 48.
/// </summary>
/// <remarks>
/// One scan, unlike ProRes, and it is the zig-zag every DCT codec since JPEG has used: the
/// coefficients run diagonally out from the DC corner so that the low frequencies, which is where a
/// picture's energy is, come first and the long tail of zeroes at the end is one run rather than
/// many.
/// </remarks>
internal static class DnxHdScan {

  /// <summary>
  /// The bitstream index of each raster position, indexed <c>[v * 8 + u]</c>, exactly as Figure 48
  /// is laid out.
  /// </summary>
  private static readonly int[] _BitstreamIndex = [
     0,  1,  5,  6, 14, 15, 27, 28,
     2,  4,  7, 13, 16, 26, 29, 42,
     3,  8, 12, 17, 25, 30, 41, 43,
     9, 11, 18, 24, 31, 40, 44, 53,
    10, 19, 23, 32, 39, 45, 52, 54,
    20, 22, 33, 38, 46, 51, 55, 60,
    21, 34, 37, 47, 50, 56, 59, 61,
    35, 36, 48, 49, 57, 58, 62, 63,
  ];

  /// <summary>
  /// The raster position of each bitstream index, which is the direction a decoder needs.
  /// </summary>
  /// <remarks>
  /// Figure 48 is printed the other way round — it maps a raster position to the index the
  /// coefficient at that position was written at — so the table a decoder wants is its inverse.
  /// Inverting it here rather than writing out the transposed numbers keeps the figure above
  /// recognisable as the one in the standard, which is the whole point of copying a table from a
  /// specification.
  /// </remarks>
  internal static readonly int[] RasterPosition = _Invert();

  private static int[] _Invert() {
    var inverse = new int[64];
    for (var position = 0; position < 64; ++position)
      inverse[_BitstreamIndex[position]] = position;

    return inverse;
  }
}
