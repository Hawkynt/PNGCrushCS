namespace FileFormat.Codecs.Dv;

/// <summary>
/// The fixed tables of the DV block layer: the two scan orders, the quantiser step table, the
/// run-level code table and the weighting matrices.
/// </summary>
/// <remarks>
/// Copied verbatim from FFmpeg's <c>libavcodec/dvdata.c</c>, <c>dvdec.c</c>, <c>dvenc.c</c> and
/// <c>mathtables.c</c> — see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this file. Not one of these
/// numbers is a choice: they are what every DV recorder ever built agreed on, and a value worked out
/// again from the standard's prose is simply a wrong value that nothing catches until real tape
/// arrives.
/// <para/>
/// The weighting matrices come in two forms because the two directions need different fixed-point
/// scalings. <see cref="InverseWeights88"/> and <see cref="InverseWeights248"/> are the reciprocals a
/// decoder multiplies by, at <c>1/2^14</c>; <see cref="ForwardWeights88"/> is the forward matrix an
/// encoder divides by, at <c>1/2^18</c>. They are not each other's reciprocals to the last bit and
/// deriving one from the other would put a rounding error into every coefficient.
/// </remarks>
internal static class DvTables {

  /// <summary>The zig-zag scan, frame mode: coefficient <c>i</c> of the stream is raster position <c>Zigzag[i]</c>.</summary>
  internal static readonly byte[] Zigzag = [
    0,   1,  8, 16,  9,  2,  3, 10,
    17, 24, 32, 25, 18, 11,  4,  5,
    12, 19, 26, 33, 40, 48, 41, 34,
    27, 20, 13,  6,  7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36,
    29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46,
    53, 60, 61, 54, 47, 55, 62, 63,
  ];

  /// <summary>The zig-zag scan, field mode — the 2-4-8 order that pairs with the 2-4-8 transform.</summary>
  /// <remarks>
  /// The fields are interleaved here rather than kept apart the way the standard writes the scan,
  /// because the 2-4-8 transform this feeds takes its input in that arrangement. The two conventions
  /// describe the same coefficients in the same order; only the address arithmetic differs.
  /// </remarks>
  internal static readonly byte[] Zigzag248 = [
     0,  8,  1,  9, 16, 24,  2, 10,
    17, 25, 32, 40, 48, 56, 33, 41,
    18, 26,  3, 11,  4, 12, 19, 27,
    34, 42, 49, 57, 50, 58, 35, 43,
    20, 28,  5, 13,  6, 14, 21, 29,
    36, 44, 51, 59, 52, 60, 37, 45,
    22, 30,  7, 15, 23, 31, 38, 46,
    53, 61, 54, 62, 39, 47, 55, 63,
  ];

  /// <summary>
  /// The quantiser: how far right each of the four coefficient areas is shifted, by quantisation
  /// number.
  /// </summary>
  /// <remarks>
  /// Indexed by the macroblock's quantisation number offset by <see cref="QuantOffsets"/> for the
  /// block's class, which is why there are twenty-two rows for a four-bit field: the class shifts the
  /// same block up to six steps coarser.
  /// </remarks>
  internal static readonly byte[][] QuantShifts = [
    [3, 3, 4, 4],
    [3, 3, 4, 4],
    [2, 3, 3, 4],
    [2, 3, 3, 4],
    [2, 2, 3, 3],
    [2, 2, 3, 3],
    [1, 2, 2, 3],
    [1, 2, 2, 3],
    [1, 1, 2, 2],
    [1, 1, 2, 2],
    [0, 1, 1, 2],
    [0, 1, 1, 2],
    [0, 0, 1, 1],
    [0, 0, 1, 1],
    [0, 0, 0, 1],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
    [0, 0, 0, 0],
  ];

  /// <summary>How many steps coarser each class number makes the quantiser.</summary>
  internal static readonly byte[] QuantOffsets = [6, 3, 0, 1];

  /// <summary>The boundaries of the four coefficient areas, in scan order.</summary>
  /// <remarks>
  /// Area 0 is coefficients 1 to 5, area 1 is 6 to 20, area 2 is 21 to 42 and area 3 is 43 to 63 —
  /// the four the quantiser shifts by different amounts, and the four an encoder budgets separately.
  /// </remarks>
  internal static readonly byte[] AreaBoundaries = [6, 21, 43, 64];

  /// <summary>The first coefficient of each of the four areas, plus the end.</summary>
  internal static readonly int[] AreaStarts = [1, 6, 21, 43, 64];

  // ==============================================================================================
  // The run-level code table
  // ==============================================================================================

  /// <summary>The number of entries in the run-level code table.</summary>
  internal const int CodeCount = 409;

  /// <summary>
  /// The code lengths, in the order the canonical codes are assigned.
  /// </summary>
  /// <remarks>
  /// This is the whole of the code book: DV assigns codes canonically in table order, so a length
  /// here plus its position is the code. Note the mapping is not one to one — <c>(1, 0)</c> can be
  /// written either as <c>0x7cf</c> or as <c>0x1f82</c> — which is why an encoder builds its own
  /// table rather than inverting the decoder's.
  /// </remarks>
  internal static readonly byte[] CodeLengths = [
     2,  3,  4,  4,  4,  4,  5,  5,  5,
     5,  6,  6,  6,  6,  7,  7,  7,
     7,  7,  7,  7,  7,  8,  8,  8,
     8,  8,  8,  8,  8,  8,  8,  8,
     8,  8,  8,  8,  8,  9,  9,  9,
     9,  9,  9,  9,  9,  9,  9,  9,
     9,  9,  9,  9,  9, 10, 10, 10,
    10, 10, 10, 10, 11, 11, 11, 11,
    11, 11, 11, 11, 12, 12, 12, 12,
    12, 12, 12, 12, 12, 12, 12, 12,
    12, 12, 12, 12, 12, 12, 12, 12,
    13, 13, 13, 13, 13, 13, 13, 13,
    13, 13, 13, 13, 13, 13, 13, 13,
    13, 13, 13, 13, 13, 13, 13, 13,
    13, 13, 13, 13, 13, 13, 13, 13,
    13, 13, 13, 13, 13, 13, 13, 13,
    13, 13, 13, 13, 13, 13, 13, 13,
    13, 13, 13, 13, 13, 13, 13, 13,
    13, 13, 13, 13, 13, 13, 13, 13,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
    15, 15, 15, 15, 15, 15, 15, 15,
  ];

  /// <summary>
  /// The run each code carries — the number of zero coefficients before the level it names.
  /// </summary>
  /// <remarks>
  /// A run of 127 is the end-of-block stamp: adding it to the coefficient position walks past the end
  /// of the block, which is exactly what "no more coefficients" means.
  /// </remarks>
  internal static readonly byte[] CodeRuns = [
     0,  0, 127, 1,  0,  0,  2,  1,  0,
     0,  3,  4,  0,  0,  5,  6,  2,
     1,  1,  0,  0,  0,  7,  8,  9,
    10,  3,  4,  2,  1,  1,  1,  0,
     0,  0,  0,  0,  0, 11, 12, 13,
    14,  5,  6,  3,  4,  2,  2,  1,
     0,  0,  0,  0,  0,  5,  3,  3,
     2,  1,  1,  1,  0,  1,  6,  4,
     3,  1,  1,  1,  2,  3,  4,  5,
     7,  8,  9, 10,  7,  8,  4,  3,
     2,  2,  2,  2,  2,  1,  1,  1,
     0,  1,  2,  3,  4,  5,  6,  7,
     8,  9, 10, 11, 12, 13, 14, 15,
    16, 17, 18, 19, 20, 21, 22, 23,
    24, 25, 26, 27, 28, 29, 30, 31,
    32, 33, 34, 35, 36, 37, 38, 39,
    40, 41, 42, 43, 44, 45, 46, 47,
    48, 49, 50, 51, 52, 53, 54, 55,
    56, 57, 58, 59, 60, 61, 62, 63,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  0,  0,  0,  0,  0,  0,
  ];

  /// <summary>
  /// The magnitude each code carries. A zero means the code states only a run, and the level that
  /// follows it is written as a code of its own.
  /// </summary>
  internal static readonly byte[] CodeLevels = [
     1,   2,   0,   1,   3,   4,   1,   2,   5,
     6,   1,   1,   7,   8,   1,   1,   2,
     3,   4,   9,  10,  11,   1,   1,   1,
     1,   2,   2,   3,   5,   6,   7,  12,
    13,  14,  15,  16,  17,   1,   1,   1,
     1,   2,   2,   3,   3,   4,   5,   8,
    18,  19,  20,  21,  22,   3,   4,   5,
     6,   9,  10,  11,   0,   0,   3,   4,
     6,  12,  13,  14,   0,   0,   0,   0,
     2,   2,   2,   2,   3,   3,   5,   7,
     7,   8,   9,  10,  11,  15,  16,  17,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   0,   0,   0,   0,   0,   0,   0,
     0,   1,   2,   3,   4,   5,   6,   7,
     8,   9,  10,  11,  12,  13,  14,  15,
    16,  17,  18,  19,  20,  21,  22,  23,
    24,  25,  26,  27,  28,  29,  30,  31,
    32,  33,  34,  35,  36,  37,  38,  39,
    40,  41,  42,  43,  44,  45,  46,  47,
    48,  49,  50,  51,  52,  53,  54,  55,
    56,  57,  58,  59,  60,  61,  62,  63,
    64,  65,  66,  67,  68,  69,  70,  71,
    72,  73,  74,  75,  76,  77,  78,  79,
    80,  81,  82,  83,  84,  85,  86,  87,
    88,  89,  90,  91,  92,  93,  94,  95,
    96,  97,  98,  99, 100, 101, 102, 103,
   104, 105, 106, 107, 108, 109, 110, 111,
   112, 113, 114, 115, 116, 117, 118, 119,
   120, 121, 122, 123, 124, 125, 126, 127,
   128, 129, 130, 131, 132, 133, 134, 135,
   136, 137, 138, 139, 140, 141, 142, 143,
   144, 145, 146, 147, 148, 149, 150, 151,
   152, 153, 154, 155, 156, 157, 158, 159,
   160, 161, 162, 163, 164, 165, 166, 167,
   168, 169, 170, 171, 172, 173, 174, 175,
   176, 177, 178, 179, 180, 181, 182, 183,
   184, 185, 186, 187, 188, 189, 190, 191,
   192, 193, 194, 195, 196, 197, 198, 199,
   200, 201, 202, 203, 204, 205, 206, 207,
   208, 209, 210, 211, 212, 213, 214, 215,
   216, 217, 218, 219, 220, 221, 222, 223,
   224, 225, 226, 227, 228, 229, 230, 231,
   232, 233, 234, 235, 236, 237, 238, 239,
   240, 241, 242, 243, 244, 245, 246, 247,
   248, 249, 250, 251, 252, 253, 254, 255,
  ];

  // ==============================================================================================
  // Weighting
  // ==============================================================================================

  /// <summary>The fractional bits the inverse weights carry.</summary>
  internal const int InverseWeightBits = 14;

  /// <summary>The fractional bits the forward weights carry.</summary>
  internal const int ForwardWeightBits = 18;

  /// <summary>The inverse weighting matrix for the 8x8 transform, in scan order, at 1/2^14.</summary>
  internal static readonly ushort[] InverseWeights88 = [
    32768, 16705, 16705, 17734, 17032, 17734, 18205, 18081,
    18081, 18205, 18725, 18562, 19195, 18562, 18725, 19266,
    19091, 19705, 19705, 19091, 19266, 21407, 19643, 20267,
    20228, 20267, 19643, 21407, 22725, 21826, 20853, 20806,
    20806, 20853, 21826, 22725, 23170, 23170, 21407, 21400,
    21407, 23170, 23170, 24598, 23786, 22018, 22018, 23786,
    24598, 25251, 24465, 22654, 24465, 25251, 25972, 25172,
    25172, 25972, 26722, 27969, 26722, 29692, 29692, 31521,
  ];

  /// <summary>The inverse weighting matrix for the 2-4-8 transform, in scan order, at 1/2^14.</summary>
  internal static readonly ushort[] InverseWeights248 = [
    32768, 16384, 16705, 16705, 17734, 17734, 17734, 17734,
    18081, 18081, 18725, 18725, 21407, 21407, 19091, 19091,
    19195, 19195, 18205, 18205, 18725, 18725, 19705, 19705,
    20267, 20267, 21826, 21826, 23170, 23170, 20806, 20806,
    20267, 20267, 19266, 19266, 21407, 21407, 20853, 20853,
    21400, 21400, 23786, 23786, 24465, 24465, 22018, 22018,
    23170, 23170, 22725, 22725, 24598, 24598, 24465, 24465,
    25172, 25172, 27969, 27969, 25972, 25972, 29692, 29692,
  ];

  /// <summary>The forward weighting matrix for the 8x8 transform, in scan order, at 1/2^18.</summary>
  internal static readonly int[] ForwardWeights88 = [
    131072, 257107, 257107, 242189, 252167, 242189, 235923, 237536,
    237536, 235923, 229376, 231390, 223754, 231390, 229376, 222935,
    224969, 217965, 217965, 224969, 222935, 200636, 218652, 211916,
    212325, 211916, 218652, 200636, 188995, 196781, 205965, 206433,
    206433, 205965, 196781, 188995, 185364, 185364, 200636, 200704,
    200636, 185364, 185364, 174609, 180568, 195068, 195068, 180568,
    174609, 170091, 175557, 189591, 175557, 170091, 165371, 170627,
    170627, 165371, 160727, 153560, 160727, 144651, 144651, 136258,
  ];
}
