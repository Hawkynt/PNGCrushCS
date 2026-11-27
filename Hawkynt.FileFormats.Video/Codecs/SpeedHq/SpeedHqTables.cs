namespace FileFormat.Codecs.SpeedHq;

/// <summary>NewTek SpeedHQ's fixed tables, copied value for value from FFmpeg.</summary>
/// <remarks>
/// <c>libavcodec/speedhq.c</c> (the AC run-level codes, little-endian), <c>speedhqdec.c</c> (the
/// quantisation matrix) and <c>mpeg12data.c</c> (MPEG-2's DC size codes, which SpeedHQ reads through a
/// little-endian reader), LGPL-2.1-or-later; see <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// </remarks>
internal static class SpeedHqTables {

  /// <summary>AC codes as FFmpeg gives them for a little-endian reader: 121 run-level pairs, then escape, then end of block.</summary>
  internal static readonly (int Code, int Length)[] AcCodes = [
    (0x0001, 2), (0x0003, 3), (0x000E, 4), (0x0007, 5), (0x0017, 5), (0x0028, 6), (0x0008, 6), (0x006F, 7),
    (0x001F, 7), (0x00C4, 8), (0x0044, 8), (0x005F, 8), (0x00DF, 8), (0x007F, 8), (0x00FF, 8), (0x3E00, 14),
    (0x1E00, 14), (0x2E00, 14), (0x0E00, 14), (0x3600, 14), (0x1600, 14), (0x2600, 14), (0x0600, 14), (0x3A00, 14),
    (0x1A00, 14), (0x2A00, 14), (0x0A00, 14), (0x3200, 14), (0x1200, 14), (0x2200, 14), (0x0200, 14), (0x0C00, 15),
    (0x7400, 15), (0x3400, 15), (0x5400, 15), (0x1400, 15), (0x6400, 15), (0x2400, 15), (0x4400, 15), (0x0400, 15),
    (0x0002, 3), (0x000C, 5), (0x004F, 7), (0x00E4, 8), (0x0004, 8), (0x0D00, 13), (0x1500, 13), (0x7C00, 15),
    (0x3C00, 15), (0x5C00, 15), (0x1C00, 15), (0x6C00, 15), (0x2C00, 15), (0x4C00, 15), (0xC800, 16), (0x4800, 16),
    (0x8800, 16), (0x0800, 16), (0x0300, 13), (0x1D00, 13), (0x0014, 5), (0x0070, 7), (0x003F, 8), (0x00C0, 10),
    (0x0500, 13), (0x0180, 12), (0x0280, 12), (0x0C80, 12), (0x0080, 12), (0x0B00, 13), (0x1300, 13), (0x001C, 5),
    (0x0064, 8), (0x0380, 12), (0x1900, 13), (0x0D80, 12), (0x0018, 6), (0x00BF, 8), (0x0480, 12), (0x0B80, 12),
    (0x0038, 6), (0x0040, 9), (0x0900, 13), (0x0030, 7), (0x0780, 12), (0x2800, 16), (0x0010, 7), (0x0A80, 12),
    (0x0050, 7), (0x0880, 12), (0x000F, 7), (0x1100, 13), (0x002F, 7), (0x0100, 13), (0x0084, 8), (0x5800, 16),
    (0x00A4, 8), (0x9800, 16), (0x0024, 8), (0x1800, 16), (0x0140, 9), (0xE800, 16), (0x01C0, 9), (0x6800, 16),
    (0x02C0, 10), (0xA800, 16), (0x0F80, 12), (0x0580, 12), (0x0980, 12), (0x0E80, 12), (0x0680, 12), (0x1F00, 13),
    (0x0F00, 13), (0x1700, 13), (0x0700, 13), (0x1B00, 13), (0xF800, 16), (0x7800, 16), (0xB800, 16), (0x3800, 16),
    (0xD800, 16), (0x0020, 6), (0x0006, 4),
  ];

  internal static readonly byte[] AcLevel = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 1, 2, 3, 4, 5, 1, 2, 3, 4, 1, 2, 3, 1, 2, 3, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];

  internal static readonly byte[] AcRun = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31];

  internal const int Escape = 121;
  internal const int EndOfBlock = 122;

  /// <summary>MPEG-2's luminance DC size codes, most significant bit first.</summary>
  internal static readonly ushort[] DcLumaCode = [0x4, 0x0, 0x1, 0x5, 0x6, 0xe, 0x1e, 0x3e, 0x7e, 0xfe, 0x1fe, 0x1ff];
  internal static readonly byte[] DcLumaBits = [3, 2, 2, 3, 3, 4, 5, 6, 7, 8, 9, 9];
  /// <summary>MPEG-2's chrominance DC size codes, most significant bit first.</summary>
  internal static readonly ushort[] DcChromaCode = [0x0, 0x1, 0x2, 0x6, 0xe, 0x1e, 0x3e, 0x7e, 0xfe, 0x1fe, 0x3fe, 0x3ff];
  internal static readonly byte[] DcChromaBits = [2, 2, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10];

  /// <summary>The quantisation matrix in raster order; its first element is always 16, unscaled.</summary>
  internal static readonly byte[] UnscaledQuant = [
    16, 16, 19, 22, 26, 27, 29, 34, 16, 16, 22, 24, 27, 29, 34, 37,
    19, 22, 26, 27, 29, 34, 34, 38, 22, 22, 26, 27, 29, 34, 37, 40,
    22, 26, 27, 29, 32, 35, 40, 48, 26, 27, 29, 32, 35, 40, 48, 58,
    26, 27, 29, 34, 38, 46, 56, 69, 27, 29, 35, 38, 46, 56, 69, 83,
  ];

  /// <summary>The zigzag scan: raster position of each scan index.</summary>
  internal static readonly byte[] Zigzag = [
    0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
    12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
  ];
}
