namespace FileFormat.Jpeg2000.Codec;

/// <summary>ITU-T T.800 Table D.2: 47-state probability estimation tables for the MQ coder.</summary>
internal static class MqTables {

  /// <summary>Number of MQ coder states.</summary>
  internal const int STATE_COUNT = 47;

  /// <summary>Qe probability values for each state.</summary>
  internal static readonly ushort[] QE = [
    0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401,
    0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
    0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201,
    0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
    0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085,
    0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
  ];

  /// <summary>Next state after MPS.</summary>
  internal static readonly byte[] NMPS = [
     1,  2,  3,  4,  5, 38,  7,  8,  9, 10, 11, 12, 13, 29, 15, 16,
    17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
    33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46,
  ];

  /// <summary>Next state after LPS.</summary>
  internal static readonly byte[] NLPS = [
     1,  6,  9, 12, 29, 33,  6, 14, 14, 14, 17, 18, 20, 21, 14, 14,
    15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
    30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46,
  ];

  /// <summary>Switch MPS/LPS sense flag (1 = switch after LPS).</summary>
  internal static readonly byte[] SWITCH = [
    1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
  ];
}
