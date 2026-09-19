namespace FileFormat.Codecs.Dv;

/// <summary>The fixed weighting and quantisation data of SMPTE 370M's DV100 block layer.</summary>
/// <remarks>
/// The values are specification-defined interoperability data and are cross-checked against FFmpeg's
/// LGPL-2.1-or-later <c>libavcodec/dvdec.c</c> and <c>dvenc.c</c>. See
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>. They are kept separate from the SD tables because DV100's
/// quantiser is not an extension of the IEC 61834-2/SMPTE 314M one: it is a different block layer.
/// </remarks>
internal static class Dv100Tables {

  /// <summary>Quantisation quanta by the four-bit QNO field. QNO 0 and 1 both mean no quantisation.</summary>
  internal static readonly byte[] QuantisationSteps = [1, 1, 2, 3, 4, 5, 6, 7, 8, 16, 18, 20, 22, 24, 28, 52];

  /// <summary>
  /// QNO/CNO combinations in increasing coarseness, used by the encoder while fitting one segment.
  /// </summary>
  /// <remarks>
  /// CNO multiplies the QNO quantum by two to its own power. The deliberately large final jump is the
  /// escape hatch the reference encoder uses when a pathological block still does not fit.
  /// </remarks>
  internal static readonly (byte QNo, byte CNo)[] QuantisationLevels = [
    (1, 0), (1, 0), (2, 0), (3, 0), (4, 0), (5, 0), (6, 0), (7, 0), (8, 0),
    (5, 1), (6, 1), (7, 1),
    (9, 0), (10, 0), (11, 0), (12, 0), (13, 0), (14, 0),
    (9, 1), (10, 1), (11, 1), (12, 1), (13, 1), (15, 0), (14, 1),
    (9, 2), (10, 2), (11, 2), (12, 2), (13, 2),
    (15, 3),
  ];

  /// <summary>Inverse 1080-line luma weights, in zig-zag order.</summary>
  internal static readonly ushort[] Inverse1080Luma = [
    128, 16, 16, 17, 17, 17, 18, 18,
    18, 18, 18, 18, 19, 18, 18, 19,
    19, 19, 19, 19, 19, 42, 38, 40,
    40, 40, 38, 42, 44, 43, 41, 41,
    41, 41, 43, 44, 45, 45, 42, 42,
    42, 45, 45, 48, 46, 43, 43, 46,
    48, 49, 48, 44, 48, 49, 101, 98,
    98, 101, 104, 109, 104, 116, 116, 123,
  ];

  /// <summary>Inverse 1080-line chroma weights, in zig-zag order.</summary>
  internal static readonly ushort[] Inverse1080Chroma = [
    128, 16, 16, 17, 17, 17, 25, 25,
    25, 25, 26, 25, 26, 25, 26, 26,
    26, 27, 27, 26, 26, 42, 38, 40,
    40, 40, 38, 42, 44, 43, 41, 41,
    41, 41, 43, 44, 91, 91, 84, 84,
    84, 91, 91, 96, 93, 86, 86, 93,
    96, 197, 191, 177, 191, 197, 203, 197,
    197, 203, 209, 219, 209, 232, 232, 246,
  ];

  /// <summary>Inverse 720-line luma weights, in zig-zag order.</summary>
  internal static readonly ushort[] Inverse720Luma = [
    128, 16, 16, 17, 17, 17, 18, 18,
    18, 18, 18, 18, 19, 18, 18, 19,
    19, 19, 19, 19, 19, 42, 38, 40,
    40, 40, 38, 42, 44, 43, 41, 41,
    41, 41, 43, 44, 68, 68, 63, 63,
    63, 68, 68, 96, 92, 86, 86, 92,
    96, 98, 96, 88, 96, 98, 202, 196,
    196, 202, 208, 218, 208, 232, 232, 246,
  ];

  /// <summary>Inverse 720-line chroma weights, in zig-zag order.</summary>
  internal static readonly ushort[] Inverse720Chroma = [
    128, 24, 24, 26, 26, 26, 36, 36,
    36, 36, 36, 36, 38, 36, 36, 38,
    38, 38, 38, 38, 38, 84, 76, 80,
    80, 80, 76, 84, 88, 86, 82, 82,
    82, 82, 86, 88, 182, 182, 168, 168,
    168, 182, 182, 192, 186, 192, 172, 186,
    192, 394, 382, 354, 382, 394, 406, 394,
    394, 406, 418, 438, 418, 464, 464, 492,
  ];

  /// <summary>Forward 1080-line luma weights, scaled as the reference integer encoder expects.</summary>
  internal static readonly int[] Forward1080Luma = [
    8192, 65536, 65536, 61681, 61681, 61681, 58254, 58254,
    58254, 58254, 58254, 58254, 55188, 58254, 58254, 55188,
    55188, 55188, 55188, 55188, 55188, 24966, 27594, 26214,
    26214, 26214, 27594, 24966, 23831, 24385, 25575, 25575,
    25575, 25575, 24385, 23831, 23302, 23302, 24966, 24966,
    24966, 23302, 23302, 21845, 22795, 24385, 24385, 22795,
    21845, 21400, 21845, 23831, 21845, 21400, 10382, 10700,
    10700, 10382, 10082, 9620, 10082, 9039, 9039, 8525,
  ];

  /// <summary>Forward 1080-line chroma weights.</summary>
  internal static readonly int[] Forward1080Chroma = [
    8192, 65536, 65536, 61681, 61681, 61681, 41943, 41943,
    41943, 41943, 40330, 41943, 40330, 41943, 40330, 40330,
    40330, 38836, 38836, 40330, 40330, 24966, 27594, 26214,
    26214, 26214, 27594, 24966, 23831, 24385, 25575, 25575,
    25575, 25575, 24385, 23831, 11523, 11523, 12483, 12483,
    12483, 11523, 11523, 10923, 11275, 12193, 12193, 11275,
    10923, 5323, 5490, 5924, 5490, 5323, 5165, 5323,
    5323, 5165, 5017, 4788, 5017, 4520, 4520, 4263,
  ];

  /// <summary>Forward 720-line luma weights.</summary>
  internal static readonly int[] Forward720Luma = [
    8192, 65536, 65536, 61681, 61681, 61681, 58254, 58254,
    58254, 58254, 58254, 58254, 55188, 58254, 58254, 55188,
    55188, 55188, 55188, 55188, 55188, 24966, 27594, 26214,
    26214, 26214, 27594, 24966, 23831, 24385, 25575, 25575,
    25575, 25575, 24385, 23831, 15420, 15420, 16644, 16644,
    16644, 15420, 15420, 10923, 11398, 12193, 12193, 11398,
    10923, 10700, 10923, 11916, 10923, 10700, 5191, 5350,
    5350, 5191, 5041, 4810, 5041, 4520, 4520, 4263,
  ];

  /// <summary>Forward 720-line chroma weights.</summary>
  internal static readonly int[] Forward720Chroma = [
    8192, 43691, 43691, 40330, 40330, 40330, 29127, 29127,
    29127, 29127, 29127, 29127, 27594, 29127, 29127, 27594,
    27594, 27594, 27594, 27594, 27594, 12483, 13797, 13107,
    13107, 13107, 13797, 12483, 11916, 12193, 12788, 12788,
    12788, 12788, 12193, 11916, 5761, 5761, 6242, 6242,
    6242, 5761, 5761, 5461, 5638, 5461, 6096, 5638,
    5461, 2661, 2745, 2962, 2745, 2661, 2583, 2661,
    2661, 2583, 2509, 2394, 2509, 2260, 2260, 2131,
  ];
}
