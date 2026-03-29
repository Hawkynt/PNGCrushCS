namespace FileFormat.G9b;

/// <summary>V9990 GFX9000 screen modes.</summary>
public enum G9bScreenMode : byte {
  /// <summary>Mode 3: 8-bit indexed (1 byte/pixel).</summary>
  Indexed8 = 3,

  /// <summary>Mode 5: 16-bit RGB555 (2 bytes/pixel LE).</summary>
  Rgb555 = 5,
}
