namespace FileFormat.Core;

/// <summary>Lightweight image metadata extracted without decoding pixel data.</summary>
public readonly record struct ImageInfo(
  int Width,
  int Height,
  int BitsPerPixel,
  string? ColorMode = null,
  string? Compression = null,
  int FrameCount = 1
);
