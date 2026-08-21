namespace FileFormat.Codecs.H263;

/// <summary>
/// The scan order of ITU-T H.263 Figure 6 and the inverse quantisation of its clause 6.2.1.
/// </summary>
/// <remarks>
/// H.263 has no weighting matrix. Where MPEG-1 multiplies every coefficient by a per-position weight
/// loaded from the sequence header, H.263 has one step size for the whole block and reconstructs a
/// level with a formula, so there is nothing here to load and nothing to un-zig-zag. The formula is
/// the whole of the arithmetic, and the part of it that is easy to leave out is the subtraction that
/// applies at even step sizes: without it every coefficient at an even QUANT is one step too far from
/// zero, which is a contrast that creeps upward through a run of predicted pictures rather than an
/// error that shows in one.
/// </remarks>
internal static class H263Quantisation {

  /// <summary>
  /// The zig-zag scan of ITU-T H.263 Figure 6: scan position to raster position within the block.
  /// </summary>
  /// <remarks>The same order MPEG-1 and JPEG use; H.263 prints it again rather than referring to them.</remarks>
  internal static readonly int[] ZigZag = [
     0,  1,  8, 16,  9,  2,  3, 10,
    17, 24, 32, 25, 18, 11,  4,  5,
    12, 19, 26, 33, 40, 48, 41, 34,
    27, 20, 13,  6,  7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36,
    29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46,
    53, 60, 61, 54, 47, 55, 62, 63,
  ];

  /// <summary>
  /// Reconstructs one coefficient other than an intra DC from its coded level (H.263, 6.2.1).
  /// </summary>
  /// <remarks>
  /// Two rules in one: the reconstruction level is always an odd multiple of the step size, and at an
  /// even step size the result is pulled one towards zero. The first is what keeps two conforming
  /// inverse transforms from drifting apart, exactly as the oddification does in MPEG-1; the second
  /// is what makes the reconstruction points of an even step size fall halfway between those of the
  /// odd one below it.
  /// </remarks>
  internal static int Dequantise(int level, int quantiser) {
    if (level == 0)
      return 0;

    var magnitude = quantiser * (2 * (level < 0 ? -level : level) + 1);
    if ((quantiser & 1) == 0)
      --magnitude;

    return _Clamp(level < 0 ? -magnitude : magnitude);
  }

  /// <summary>
  /// Reconstructs the DC coefficient of an intra block, which is coded as a value and not as a level
  /// (H.263, 5.4.1 and 6.2.1).
  /// </summary>
  /// <remarks>
  /// The step is a fixed eight and does not depend on QUANT, and the eight-bit field is not a plain
  /// number: nought and one-hundred-and-twenty-eight are not used because they would help a coded
  /// block look like a start code, and two-hundred-and-fifty-five stands in for the level of
  /// one-hundred-and-twenty-eight that the second of them would otherwise have carried.
  /// </remarks>
  internal static int DequantiseIntraDc(int intraDc) => (intraDc == 255 ? 128 : intraDc) * 8;

  /// <summary>Saturates to the range a reconstructed coefficient is defined over (H.263, 6.2.1).</summary>
  private static int _Clamp(int value) => value < -2048 ? -2048 : value > 2047 ? 2047 : value;
}
