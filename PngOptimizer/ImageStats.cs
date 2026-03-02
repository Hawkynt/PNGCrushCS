namespace PngOptimizer;

/// <summary>Image statistics record</summary>
public readonly record struct ImageStats(
  int UniqueColors,
  int UniqueArgbColors,
  bool HasAlpha,
  bool IsGrayscale,
  (byte R, byte G, byte B)? TransparentKeyColor = null,
  byte? TransparentKeyGray = null
);
