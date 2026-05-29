namespace FileFormat.Jpeg;

/// <summary>8x8 block of DCT coefficients in zigzag order.</summary>
internal sealed class JpegCoefficientBlock {
  public readonly short[] Coefficients = new short[64];
}
