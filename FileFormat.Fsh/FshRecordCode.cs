namespace FileFormat.Fsh;

/// <summary>Record codes identifying the pixel format of an FSH entry.</summary>
public enum FshRecordCode : byte {
  /// <summary>DXT1 block-compressed (4 bits/pixel).</summary>
  Dxt1 = 0x60,

  /// <summary>DXT3 block-compressed (8 bits/pixel).</summary>
  Dxt3 = 0x61,

  /// <summary>16-bit ARGB4444.</summary>
  Argb4444 = 0x6D,

  /// <summary>32-bit ARGB8888 (variant 0x78).</summary>
  Argb8888_78 = 0x78,

  /// <summary>8-bit indexed with 1024-byte BGRA palette.</summary>
  Indexed8 = 0x7B,

  /// <summary>32-bit ARGB8888.</summary>
  Argb8888 = 0x7D,

  /// <summary>16-bit ARGB1555.</summary>
  Argb1555 = 0x7E,

  /// <summary>24-bit RGB888.</summary>
  Rgb888 = 0x7F,

  /// <summary>16-bit RGB565 (no alpha).</summary>
  Rgb565 = 0x80,
}
