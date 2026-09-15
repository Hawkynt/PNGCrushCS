using System.Numerics;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The normative probability tables both directions of the arithmetic coder run on — ITU-T H.265,
/// Tables 9-46 and 9-47.
/// </summary>
/// <remarks>
/// They live here rather than in either the decoder or the encoder because the two must agree on
/// every entry. A coder that reads one table and a coder that writes another diverge on the first
/// bin where they differ and never resynchronise, and nothing about the resulting stream says so:
/// it decodes, it produces samples, and the samples are not the ones that were encoded. One copy
/// makes that class of disagreement impossible rather than merely unlikely.
/// </remarks>
internal static class H265CabacTables {

  /// <summary>
  /// Table 9-46: what the interval gives up to the less probable symbol, by state and by how wide
  /// the interval currently is.
  /// </summary>
  /// <remarks>
  /// The interval is quantised to four buckets by its top two significant bits, so one table of
  /// sixty-four states by four widths covers every interval either direction can be in.
  /// </remarks>
  internal static readonly byte[] RangeLps = [
    128, 176, 208, 240, 128, 167, 197, 227, 128, 158, 187, 216, 123, 150, 178, 205,
    116, 142, 169, 195, 111, 135, 160, 185, 105, 128, 152, 175, 100, 122, 144, 166,
    95, 116, 137, 158, 90, 110, 130, 150, 85, 104, 123, 142, 81, 99, 117, 135,
    77, 94, 111, 128, 73, 89, 105, 122, 69, 85, 100, 116, 66, 80, 95, 110,
    62, 76, 90, 104, 59, 72, 86, 99, 56, 69, 81, 94, 53, 65, 77, 89,
    51, 62, 73, 85, 48, 59, 69, 80, 46, 56, 66, 76, 43, 53, 63, 72,
    41, 50, 59, 69, 39, 48, 56, 65, 37, 45, 54, 62, 35, 43, 51, 59,
    33, 41, 48, 56, 32, 39, 46, 53, 30, 37, 43, 50, 29, 35, 41, 48,
    27, 33, 39, 45, 26, 31, 37, 43, 24, 30, 35, 41, 23, 28, 33, 39,
    22, 27, 32, 37, 21, 26, 30, 35, 20, 24, 29, 33, 19, 23, 27, 31,
    18, 22, 26, 30, 17, 21, 25, 28, 16, 20, 23, 27, 15, 19, 22, 25,
    14, 18, 21, 24, 14, 17, 20, 23, 13, 16, 19, 22, 12, 15, 18, 21,
    12, 14, 17, 20, 11, 14, 16, 19, 11, 13, 15, 18, 10, 12, 15, 17,
    10, 12, 14, 16, 9, 11, 13, 15, 9, 11, 12, 14, 8, 10, 12, 14,
    8, 9, 11, 13, 7, 9, 11, 12, 7, 9, 10, 12, 7, 8, 10, 11,
    6, 8, 9, 11, 6, 7, 9, 10, 6, 7, 8, 9, 2, 2, 2, 2,
  ];

  /// <summary>
  /// Table 9-47: where a state moves when the less probable symbol arrives.
  /// </summary>
  /// <remarks>
  /// It moves several steps at once, and further the more confident it was, because being wrong when
  /// confident is stronger evidence than being right when unsure. State 63 stays put: it is the
  /// terminating state, whose interval is fixed at two.
  /// </remarks>
  internal static readonly byte[] TransitionLps = [
    0, 0, 1, 2, 2, 4, 4, 5, 6, 7, 8, 9, 9, 11, 11, 12,
    13, 13, 15, 15, 16, 16, 18, 18, 19, 19, 21, 21, 22, 22, 23, 24,
    24, 25, 26, 26, 27, 27, 28, 29, 29, 30, 30, 30, 31, 32, 32, 33,
    33, 33, 34, 34, 35, 35, 35, 36, 36, 36, 37, 37, 37, 38, 38, 63,
  ];

  /// <summary>Table 9-47: where a state moves when the more probable symbol arrives — one step up.</summary>
  internal static readonly byte[] TransitionMps = [
    1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
    17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
    33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
    49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 62, 63,
  ];

  /// <summary>
  /// How many doublings bring an interval of <paramref name="range"/> back into [256, 512).
  /// </summary>
  /// <remarks>
  /// The decoder discovers this by looping until the interval is wide enough again; the encoder has
  /// to know the count up front, because it shifts the same number of bits out of its low end in one
  /// step. Computed rather than tabulated: a table would be one more pair of numbers that has to
  /// agree with the loop on the other side, and this cannot disagree with it.
  /// </remarks>
  internal static int RenormalizationShift(int range) => 8 - BitOperations.Log2((uint)range);
}
