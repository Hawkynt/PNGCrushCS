namespace FileFormat.RamBrandt;

/// <summary>The ANTIC mode a Ram Brandt screen was drawn in; the file extension is what names it.</summary>
public enum RamBrandtMode {

  /// <summary>Graphics 7 (.rm0): 160x96 in four colours, every pixel doubled in both directions.</summary>
  Graphics7 = 0,

  /// <summary>Graphics 9 (.rm1): 80x192, sixteen luminances of the background hue.</summary>
  Graphics9 = 1,

  /// <summary>Graphics 10 (.rm2): 80x192, nine colours taken straight from the GTIA registers.</summary>
  Graphics10 = 2,

  /// <summary>Graphics 11 (.rm3): 80x192, sixteen hues at the background luminance.</summary>
  Graphics11 = 3,

  /// <summary>Graphics 15 (.rm4): 160x192 in four colours.</summary>
  Graphics15 = 4,
}
