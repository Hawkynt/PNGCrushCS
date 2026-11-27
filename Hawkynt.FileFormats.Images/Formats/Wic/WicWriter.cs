using System;

namespace FileFormat.Wic;

/// <summary>Writes JWIC files the way the BeOS translators did.</summary>
internal static class WicWriter {

  /// <summary>
  /// The widest padded side the 1.01 translator can transform: its line buffers hold 4096 samples
  /// minus a 10-sample guard, and padding is to multiples of 16.
  /// </summary>
  public const int MaxMatrixSide = 4080;

  public static byte[] ToBytes(WicFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    if (file.Width < 1 || file.Height < 1)
      throw new ArgumentException("JWIC picture dimensions must be positive.", nameof(file));

    var colour = !file.IsGrayscale;
    var expected = file.Width * file.Height * (colour ? 3 : 1);
    if (file.PixelData.Length < expected)
      throw new ArgumentException($"JWIC pixel data holds {file.PixelData.Length} bytes; {expected} are needed.", nameof(file));

    if (file.Transform is not (WicTransform.Daubechies4 or WicTransform.Biorthogonal97 or WicTransform.Haar))
      throw new ArgumentException($"JWIC transform {(int)file.Transform} is not defined.", nameof(file));

    if (file.Transform == WicTransform.Haar && file.Generation == WicGeneration.Version100)
      throw new ArgumentException("The Haar transform arrived with the 1.01 translator; a 1.0 header cannot carry it.", nameof(file));

    if (!(file.BitsPerPixelBudget > 0) || float.IsInfinity(file.BitsPerPixelBudget))
      throw new ArgumentException("The JWIC bit budget must be a positive number of bits per pixel.", nameof(file));

    if (file.ChromaBudgetScale is < 0 or > 128)
      throw new ArgumentException("The JWIC chroma budget scale lies between 0 and 128.", nameof(file));

    if (file.Dpi is < 0 or > ushort.MaxValue)
      throw new ArgumentException("The JWIC resolution field is 16 bits.", nameof(file));

    var matrixWidth = _Pad(file.Width);
    var matrixHeight = _Pad(file.Height);

    // A colour picture's chroma planes are half as wide and still need one decomposition level,
    // which takes an even width of at least 8 — so the matrix is at least 16 wide.
    if (colour)
      matrixWidth = Math.Max(matrixWidth, 16);

    if (matrixWidth > MaxMatrixSide || matrixHeight > MaxMatrixSide)
      throw new ArgumentException(
        $"A {file.Width}x{file.Height} picture pads to {matrixWidth}x{matrixHeight}; JWIC decoders transform at most {MaxMatrixSide} samples a side.",
        nameof(file));

    var template = new WicHeader(
      file.Generation,
      file.Width,
      file.Height,
      file.Dpi,
      (uint)file.ChromaBudgetScale,
      colour ? WicHeader.Colour : WicHeader.Grey,
      file.Transform,
      matrixWidth,
      matrixHeight,
      0,
      0,
      colour && file.Generation == WicGeneration.Version101 ? 0x00080808u : 0x08u,
      0);

    return WicCodec.Encode(template, file.PixelData, file.BitsPerPixelBudget);
  }

  /// <summary>The translators' padding: multiples of 16 above 256 samples, of 8 otherwise.</summary>
  private static int _Pad(int size) => size > 256 ? (size + 15) & ~15 : (size + 7) & ~7;
}
