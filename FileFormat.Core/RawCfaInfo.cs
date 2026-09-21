using System;

namespace FileFormat.Core;

/// <summary>The four two-by-two Bayer color-filter-array phases.</summary>
public enum RawCfaPattern {
  /// <summary>Top row R,G; bottom row G,B.</summary>
  Rggb,
  /// <summary>Top row G,R; bottom row B,G.</summary>
  Grbg,
  /// <summary>Top row G,B; bottom row R,G.</summary>
  Gbrg,
  /// <summary>Top row B,G; bottom row G,R.</summary>
  Bggr,
}

/// <summary>Semantic interpretation of a <see cref="PixelFormat.Cfa16"/> sensor mosaic.</summary>
public readonly record struct RawCfaInfo {
  /// <summary>The two-by-two Bayer phase at image coordinate (0,0).</summary>
  public RawCfaPattern Pattern { get; }

  /// <summary>Number of significant right-justified bits in each stored ushort sample.</summary>
  public int BitDepth { get; }

  public RawCfaInfo(RawCfaPattern pattern, int bitDepth) {
    if (!Enum.IsDefined(pattern))
      throw new ArgumentOutOfRangeException(nameof(pattern));
    if (bitDepth is < 1 or > 16)
      throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "CFA samples stored in Cfa16 support 1 through 16 significant bits.");

    this.Pattern = pattern;
    this.BitDepth = bitDepth;
  }
}
