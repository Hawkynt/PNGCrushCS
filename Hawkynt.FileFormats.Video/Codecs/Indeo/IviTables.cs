namespace FileFormat.Codecs.Indeo;

/// <summary>
/// The scan patterns and the built-in Huffman codebook descriptors that Indeo 4 and Indeo 5 share.
/// </summary>
/// <remarks>
/// Everything here is a fixed table of the format rather than something a stream carries. A stream
/// selects among these by index — which scan a band uses, which codebook its blocks are coded with —
/// so a table that differed from these by one entry would decode most of a frame correctly and the
/// rest into noise.
/// </remarks>
internal static class IviTables {

  /// <summary>
  /// The zigzag order, which Indeo 5's two-dimensional band uses and which Indeo 4 names as its scan
  /// patterns zero and four.
  /// </summary>
  /// <remarks>
  /// JPEG's zigzag exactly, and indexed the same way round: the entry at scan position <i>k</i> is the
  /// offset within the block that the <i>k</i>-th coded coefficient belongs at.
  /// </remarks>
  internal static readonly byte[] ZigzagDirect = [
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
  /// Column-major order, for a band whose transform runs down columns only.
  /// </summary>
  internal static readonly byte[] VerticalScan8x8 = [
     0,  8, 16, 24, 32, 40, 48, 56,
     1,  9, 17, 25, 33, 41, 49, 57,
     2, 10, 18, 26, 34, 42, 50, 58,
     3, 11, 19, 27, 35, 43, 51, 59,
     4, 12, 20, 28, 36, 44, 52, 60,
     5, 13, 21, 29, 37, 45, 53, 61,
     6, 14, 22, 30, 38, 46, 54, 62,
     7, 15, 23, 31, 39, 47, 55, 63,
  ];

  /// <summary>
  /// Row-major order, for a band whose transform runs along rows only, and for a band that is not
  /// transformed at all.
  /// </summary>
  internal static readonly byte[] HorizontalScan8x8 = [
     0,  1,  2,  3,  4,  5,  6,  7,
     8,  9, 10, 11, 12, 13, 14, 15,
    16, 17, 18, 19, 20, 21, 22, 23,
    24, 25, 26, 27, 28, 29, 30, 31,
    32, 33, 34, 35, 36, 37, 38, 39,
    40, 41, 42, 43, 44, 45, 46, 47,
    48, 49, 50, 51, 52, 53, 54, 55,
    56, 57, 58, 59, 60, 61, 62, 63,
  ];

  /// <summary>The four-by-four zigzag, for the chrominance band of a scalable picture.</summary>
  internal static readonly byte[] DirectScan4x4 = [
     0,  1,  4,  8,  5,  2,  3,  6,  9, 12, 13, 10,  7, 11, 14, 15,
  ];

  /// <summary>
  /// The eight built-in codebook descriptors for macroblock signals: quantiser deltas and motion
  /// vector deltas.
  /// </summary>
  /// <remarks>
  /// A stream selects one of these with a three-bit index, or the value seven to spell a descriptor
  /// out for itself. Index seven of this array is therefore also the default when a picture header
  /// declines to select at all, which is why there are eight of them and not seven.
  /// </remarks>
  internal static readonly byte[][] MacroblockDescriptors = [
    [0, 4, 5, 4, 4, 4, 6, 6],
    [0, 2, 2, 3, 3, 3, 3, 5, 3, 2, 2, 2],
    [0, 2, 3, 4, 3, 3, 3, 3, 4, 3, 2, 2],
    [0, 3, 4, 4, 3, 3, 3, 3, 3, 2, 2, 2],
    [0, 4, 4, 3, 3, 3, 3, 2, 3, 3, 2, 1, 1],
    [0, 4, 4, 4, 4, 3, 3, 3, 2],
    [0, 4, 4, 4, 4, 3, 3, 2, 2, 2],
    [0, 4, 4, 4, 3, 3, 2, 3, 2, 2, 2, 2],
  ];

  /// <summary>
  /// The eight built-in codebook descriptors for block signals: the run-value symbols of the
  /// transform coefficients.
  /// </summary>
  internal static readonly byte[][] BlockDescriptors = [
    [1, 2, 3, 4, 4, 7, 5, 5, 4, 1],
    [2, 3, 4, 4, 4, 7, 5, 4, 3, 3, 2],
    [2, 4, 5, 5, 5, 5, 6, 4, 4, 3, 1, 1],
    [3, 3, 4, 4, 5, 6, 6, 4, 4, 3, 2, 1, 1],
    [3, 4, 4, 5, 5, 5, 6, 5, 4, 2, 2],
    [3, 4, 5, 5, 5, 5, 6, 4, 3, 3, 2, 1, 1],
    [3, 4, 5, 5, 5, 6, 5, 4, 3, 3, 2, 1, 1],
    [3, 4, 4, 5, 5, 5, 6, 5, 5],
  ];
}
