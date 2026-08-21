namespace FileFormat.Codecs.ProRes;

/// <summary>
/// The two orders a block's coefficients are visited in, and the arrangement of a slice's blocks.
/// </summary>
/// <remarks>
/// RDD 36:2022, 7.2. There are two scans and which one applies is a property of the picture, not of
/// the block: 7.2.2 selects the progressive pattern when <c>interlace_mode</c> is 0 and the
/// interlaced one otherwise.
/// <para/>
/// Neither is the zig-zag of JPEG or MPEG, and neither is the other's transpose. Reading a
/// progressive frame with the interlaced pattern produces a picture whose low frequencies are in the
/// right place and whose detail is scrambled — a decode that looks nearly right, which is why the
/// tables are written out from the specification's own figures rather than generated from a rule
/// that seemed to fit the first few entries.
/// </remarks>
internal static class ProResScan {

  /// <summary>
  /// Scanned frequency index of each raster position, for a frame picture.
  /// </summary>
  /// <remarks>
  /// RDD 36:2022, Figure 4. Indexed <c>[v * 8 + u]</c>, where <c>u</c> is the horizontal frequency
  /// and <c>v</c> the vertical one, exactly as the figure is laid out.
  /// </remarks>
  internal static readonly int[] Progressive = [
     0,  1,  4,  5, 16, 17, 21, 22,
     2,  3,  6,  7, 18, 20, 23, 28,
     8,  9, 12, 13, 19, 24, 27, 29,
    10, 11, 14, 15, 25, 26, 30, 31,
    32, 33, 37, 38, 45, 46, 53, 54,
    34, 36, 39, 44, 47, 52, 55, 60,
    35, 40, 43, 48, 51, 56, 59, 61,
    41, 42, 49, 50, 57, 58, 62, 63,
  ];

  /// <summary>
  /// Scanned frequency index of each raster position, for a field picture.
  /// </summary>
  /// <remarks>
  /// RDD 36:2022, Figure 5. A field carries every other row of the frame, so its vertical detail is
  /// spread differently across the frequencies and the scan that visits them in decreasing order of
  /// likely magnitude is a different one.
  /// </remarks>
  internal static readonly int[] Interlaced = [
     0,  2,  8, 10, 32, 34, 35, 41,
     1,  3,  9, 11, 33, 36, 40, 42,
     4,  6, 12, 14, 37, 39, 43, 49,
     5,  7, 13, 15, 38, 44, 48, 50,
    16, 18, 19, 25, 45, 47, 51, 57,
    17, 20, 24, 26, 46, 52, 56, 58,
    21, 23, 27, 30, 53, 55, 59, 62,
    22, 28, 29, 31, 54, 60, 61, 63,
  ];
}
