namespace FileFormat.Core;

/// <summary>Describes the pixel layout and bit depth of raw image data.</summary>
public enum PixelFormat {
  Bgra32,
  Rgba32,
  Argb32,
  Rgb24,
  Bgr24,
  Gray8,
  Gray16,
  GrayAlpha16,
  Indexed8,
  Indexed4,
  Indexed1,
  Rgba64,
  Rgb48,
  Rgb565,
}
