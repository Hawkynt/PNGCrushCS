namespace FileFormat.Tga;

public enum TgaColorMode {
  Original = 0,
  Rgba32 = 1,
  Rgb24 = 2,
  Grayscale8 = 3,
  Indexed8 = 4,

  /// <summary>
  /// Sixteen bits a pixel: five each of red, green and blue with one bit left over for attribute.
  /// </summary>
  /// <remarks>
  /// This had no member of its own, so a sixteen-bit picture fell through to the catch-all and was
  /// drawn as eight-bit greyscale — the right shape and size, in grey, at half the width's worth of
  /// samples.
  /// </remarks>
  Rgb16_555 = 5
}
