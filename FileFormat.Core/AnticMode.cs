namespace FileFormat.Core;

/// <summary>What ANTIC is fetching for a scanline, which decides how GTIA reads it.</summary>
public enum AnticMode {

  /// <summary>Nothing: the line shows the background and whatever sprites cross it.</summary>
  Blank,

  /// <summary>Two bits a pixel against the background and three playfield registers.</summary>
  FourColor,

  /// <summary>As four-colour, but a character's high bit swaps its third colour for the fourth.</summary>
  FiveColor,

  /// <summary>One bit a pixel at twice the resolution, taking a hue from one register and a luminance from another.</summary>
  HiRes,
}
