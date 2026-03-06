namespace FileFormat.Png;

/// <summary>Color types for PNG images as defined in the PNG specification</summary>
public enum PngColorType : byte {
  Grayscale = 0,
  RGB = 2,
  Palette = 3,
  GrayscaleAlpha = 4,
  RGBA = 6
}
