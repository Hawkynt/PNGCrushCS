namespace FileFormat.WebP;

/// <summary>Feature flags and dimensions extracted from a WebP file.</summary>
public readonly record struct WebPFeatures(
  int Width,
  int Height,
  bool HasAlpha,
  bool IsLossless,
  bool IsAnimated
);
