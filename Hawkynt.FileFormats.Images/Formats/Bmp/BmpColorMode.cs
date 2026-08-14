namespace FileFormat.Bmp;

public enum BmpColorMode {
  Original = 0,
  Rgb24 = 1,
  Rgb16_565 = 2,
  Palette8 = 3,
  Palette4 = 4,
  Palette1 = 5,
  Grayscale8 = 6,

  /// <summary>Four bytes a pixel, blue-green-red-alpha, the fourth carrying transparency.</summary>
  /// <remarks>
  /// A 32-bit file whose fourth byte turns out to be padding is reported as <see cref="Rgb24"/>
  /// instead, since that is what it holds once the padding is dropped. Which of the two a given file
  /// is cannot be told from <c>biCompression</c>; see <c>BmpReader._CarriesAlpha</c> for the rule and
  /// what it was measured against.
  /// </remarks>
  Bgra32 = 7
}
