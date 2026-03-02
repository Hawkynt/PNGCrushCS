using System;

namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  // RFC 1951 constant tables

  /// <summary>Number of length codes (257-285)</summary>
  internal const int NUM_LENGTH_CODES = 29;

  /// <summary>Number of distance codes (0-29)</summary>
  internal const int NUM_DISTANCE_CODES = 30;

  // Precomputed lookup: _lengthCodeTable[length - 3] = code index (0-28) for lengths 3-258
  private static readonly byte[] _lengthCodeTable = _BuildLengthCodeTable();

  // Precomputed lookup: _distCodeTable[distance] = code (0-29) for distances 1-512
  private static readonly byte[] _distCodeTable = _BuildDistCodeTable();

  /// <summary>Base lengths for codes 257-285</summary>
  internal static ReadOnlySpan<ushort> LengthBase => [
    3, 4, 5, 6, 7, 8, 9, 10, 11, 13,
    15, 17, 19, 23, 27, 31, 35, 43, 51, 59,
    67, 83, 99, 115, 131, 163, 195, 227, 258
  ];

  /// <summary>Extra bits for length codes 257-285</summary>
  internal static ReadOnlySpan<byte> LengthExtraBits => [
    0, 0, 0, 0, 0, 0, 0, 0, 1, 1,
    1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
    4, 4, 4, 4, 5, 5, 5, 5, 0
  ];

  /// <summary>Base distances for distance codes 0-29</summary>
  internal static ReadOnlySpan<ushort> DistanceBase => [
    1, 2, 3, 4, 5, 7, 9, 13, 17, 25,
    33, 49, 65, 97, 129, 193, 257, 385, 513, 769,
    1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577
  ];

  /// <summary>Extra bits for distance codes 0-29</summary>
  internal static ReadOnlySpan<byte> DistanceExtraBits => [
    0, 0, 0, 0, 1, 1, 2, 2, 3, 3,
    4, 4, 5, 5, 6, 6, 7, 7, 8, 8,
    9, 9, 10, 10, 11, 11, 12, 12, 13, 13
  ];

  /// <summary>Code-length alphabet order per RFC 1951</summary>
  internal static ReadOnlySpan<byte> CodeLengthOrder => [
    16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
  ];

  private static byte[] _BuildLengthCodeTable() {
    ReadOnlySpan<ushort> bases = [
      3, 4, 5, 6, 7, 8, 9, 10, 11, 13,
      15, 17, 19, 23, 27, 31, 35, 43, 51, 59,
      67, 83, 99, 115, 131, 163, 195, 227, 258
    ];
    var table = new byte[256]; // lengths 3-258
    for (var code = 0; code < NUM_LENGTH_CODES; ++code) {
      var lo = (int)bases[code];
      var hi = code < NUM_LENGTH_CODES - 1 ? bases[code + 1] - 1 : 258;
      for (var len = lo; len <= hi; ++len)
        table[len - 3] = (byte)code;
    }

    return table;
  }

  /// <summary>Get the length code (257-285) for a given match length (3-258)</summary>
  internal static int GetLengthCode(int length) {
    return 257 + _lengthCodeTable[length - 3];
  }

  private static byte[] _BuildDistCodeTable() {
    ReadOnlySpan<ushort> bases = [
      1, 2, 3, 4, 5, 7, 9, 13, 17, 25,
      33, 49, 65, 97, 129, 193, 257, 385, 513, 769,
      1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577
    ];
    var table = new byte[513]; // distances 1-512
    for (var code = 0; code < NUM_DISTANCE_CODES; ++code) {
      var lo = (int)bases[code];
      var hi = code < NUM_DISTANCE_CODES - 1 ? bases[code + 1] - 1 : 32768;
      for (var d = lo; d <= Math.Min(hi, 512); ++d)
        table[d] = (byte)code;
    }

    return table;
  }

  /// <summary>Get the distance code (0-29) for a given distance (1-32768)</summary>
  internal static int GetDistanceCode(int distance) {
    if (distance <= 512)
      return _distCodeTable[distance];

    // Binary search for large distances (codes 18-29)
    var bases = DistanceBase;
    var lo = 18;
    var hi = NUM_DISTANCE_CODES - 1;
    while (lo < hi) {
      var mid = (lo + hi + 1) >> 1;
      if (bases[mid] <= distance)
        lo = mid;
      else
        hi = mid - 1;
    }

    return lo;
  }

  /// <summary>Get the number of extra bits for a length code (257-285)</summary>
  internal static int GetLengthExtraBitCount(int lengthCode) => LengthExtraBits[lengthCode - 257];

  /// <summary>Get the extra bits value for a given match length</summary>
  internal static int GetLengthExtraBitValue(int length, int lengthCode) => length - LengthBase[lengthCode - 257];

  /// <summary>Get the number of extra bits for a distance code</summary>
  internal static int GetDistanceExtraBitCount(int distCode) => DistanceExtraBits[distCode];

  /// <summary>Get the extra bits value for a given distance</summary>
  internal static int GetDistanceExtraBitValue(int distance, int distCode) => distance - DistanceBase[distCode];
}
