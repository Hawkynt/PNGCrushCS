namespace FileFormat.AtariIce;

/// <summary>How one field of an Interlace Character Editor picture is to be read.</summary>
/// <remarks>
/// The first half of the name is the ANTIC mode the characters are drawn in and the second, where
/// there is one, is the GTIA mode laid over it. The two are independent: ANTIC decides how many
/// bits a pixel takes from the character set, GTIA decides what those bits mean, and the editor's
/// whole trick is pairing one of each — including pairing a different one in each field, so that
/// the two together show colours neither could.
/// </remarks>
public enum IceFrameMode {

  /// <summary>Mode 0: one bit a pixel, two colours.</summary>
  Gr0,

  /// <summary>Mode 0 read as GTIA 9: a nibble is a luminance.</summary>
  Gr0Gtia9,

  /// <summary>Mode 0 read as GTIA 10: a nibble indexes the colour registers.</summary>
  Gr0Gtia10,

  /// <summary>Mode 0 read as GTIA 11: a nibble is a hue.</summary>
  Gr0Gtia11,

  /// <summary>Mode 12: two bits a pixel, four colours.</summary>
  Gr12,

  /// <summary>Mode 12 read as GTIA 9.</summary>
  Gr12Gtia9,

  /// <summary>Mode 12 read as GTIA 10.</summary>
  Gr12Gtia10,

  /// <summary>Mode 12 read as GTIA 11.</summary>
  Gr12Gtia11,

  /// <summary>Mode 13, which is mode 12 with every character row covering two scanlines.</summary>
  Gr13Gtia9,

  /// <summary>Mode 13 read as GTIA 10.</summary>
  Gr13Gtia10,

  /// <summary>Mode 13 read as GTIA 11.</summary>
  Gr13Gtia11,
}
