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
  /// <summary>Indexed image with 16-bit little-endian indices into a palette of up to 65 536 entries.
  /// Use this for formats whose native palette exceeds 256 colours (CGA Reenigne 1024-mode, 10-bit
  /// indexed scientific scans, anything wanting 9–16-bit indexed depth).</summary>
  Indexed16,
  Rgba64,
  Rgb48,
  Rgb565,
}
