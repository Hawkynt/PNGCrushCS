namespace FileFormat.AtariAgp;

/// <summary>The ANTIC mode an AGP file's first byte names.</summary>
/// <remarks>
/// The file states its mode outright rather than leaving it to be guessed from the length: every
/// AGP file is the same 7690 bytes whichever mode it holds, because the bitmap is always a full
/// 40-by-192 screen and only the reading of it differs.
/// </remarks>
public enum AtariAgpMode {

  /// <summary>Graphics 8: one bit a pixel, 320 across, two colours sharing a hue.</summary>
  Graphics8 = 8,

  /// <summary>Graphics 9: a nibble a pixel, sixteen luminances of one hue.</summary>
  Graphics9 = 9,

  /// <summary>Graphics 10: a nibble a pixel selecting one of nine colour registers.</summary>
  Graphics10 = 10,

  /// <summary>Graphics 11: a nibble a pixel, sixteen hues at one luminance.</summary>
  Graphics11 = 11,

  /// <summary>Graphics 15 (ANTIC E): two bits a pixel, four registers, 160 logical pixels across.</summary>
  Graphics15 = 15,
}
