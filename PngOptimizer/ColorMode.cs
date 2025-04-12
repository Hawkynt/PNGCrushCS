namespace PngOptimizer;

/// <summary>Color modes for PNG images</summary>
public enum ColorMode:byte {
  Grayscale = 0,
  RGB = 2,
  Palette = 3,
  GrayscaleAlpha = 4,
  RGBA = 6
}
