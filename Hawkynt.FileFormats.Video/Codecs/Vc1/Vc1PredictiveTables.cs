namespace FileFormat.Codecs.Vc1;

/// <summary>Specification-defined tables used by progressive P and B pictures.</summary>
/// <remarks>
/// These values are data, not an implementation: Tables 236, 246 and the P/B CBPCY table set of SMPTE 421M.
/// The first writer deliberately selects table zero, while the decoder keeps the selector explicit so the other
/// normative tables can be added without changing macroblock parsing.
/// </remarks>
internal static class Vc1PredictiveTables {

  /// <summary>Table 246: motion-vector differential VLC table zero.</summary>
  internal static ReadOnlySpan<int> MotionVectorDifferential0 => [
    0, 6,   2, 7,   3, 7,   8, 8,   576, 14,   3, 6,
    2, 5,   6, 6,   5, 7,   577, 14,   578, 14,   7, 6,
    8, 6,   9, 6,   40, 8,   19, 9,   37, 10,   82, 9,
    21, 7,   22, 7,   23, 7,   579, 14,   580, 14,   166, 10,
    96, 9,   167, 10,   49, 8,   194, 10,   195, 10,   581, 14,
    582, 14,   583, 14,   292, 13,   293, 13,   294, 13,   13, 6,
    2, 3,   7, 5,   24, 6,   50, 8,   102, 9,   295, 13,
    13, 5,   7, 4,   8, 4,   18, 5,   50, 7,   103, 9,
    38, 6,   20, 5,   21, 5,   22, 5,   39, 6,   204, 9,
    103, 8,   23, 5,   24, 5,   25, 5,   104, 7,   410, 10,
    105, 7,   106, 7,   107, 7,   108, 7,   109, 7,   220, 8,
    411, 10,   442, 9,   222, 8,   443, 9,   446, 9,   447, 9,
    7, 3,
  ];

  /// <summary>P/B coded-block-pattern VLC table zero. The entry index is the six-bit block pattern.</summary>
  internal static ReadOnlySpan<int> CodedBlockPattern0 => [
    0, 13,   6, 13,   15, 7,   13, 13,   13, 7,   11, 13,
    3, 13,   13, 12,   5, 6,   8, 13,   49, 7,   10, 12,
    12, 6,   114, 8,   102, 8,   119, 8,   1, 5,   54, 7,
    96, 8,   8, 12,   10, 6,   111, 8,   5, 13,   15, 12,
    12, 7,   10, 13,   2, 13,   12, 12,   13, 6,   115, 8,
    53, 7,   63, 7,   1, 6,   7, 13,   1, 8,   7, 12,
    14, 7,   12, 13,   4, 13,   14, 12,   1, 7,   9, 13,
    97, 8,   11, 12,   7, 5,   58, 7,   52, 7,   62, 7,
    4, 6,   103, 8,   1, 13,   9, 12,   11, 6,   56, 7,
    101, 8,   118, 8,   4, 5,   110, 8,   100, 8,   30, 6,
    2, 3,   5, 3,   4, 3,   3, 2,
  ];

  /// <summary>Table 236: inter 8x8 scan for Simple/Main and progressive Advanced profile.</summary>
  internal static ReadOnlySpan<byte> Inter8x8Scan => [
    0, 8, 1, 2, 9, 16, 24, 17,
    10, 3, 4, 11, 18, 25, 32, 40,
    48, 56, 41, 33, 26, 19, 12, 5,
    6, 13, 20, 27, 34, 49, 57, 58,
    50, 42, 35, 28, 21, 14, 7, 15,
    22, 29, 36, 43, 51, 59, 60, 52,
    44, 37, 30, 23, 31, 38, 45, 53,
    61, 62, 54, 46, 39, 47, 55, 63,
  ];
}
