namespace FileFormat.Jpeg;

/// <summary>Quantization/dequantization helpers and IJG quality scaling.</summary>
internal static class JpegQuantizer {

  /// <summary>Builds a quantization table for encoding at the given quality (1-100).</summary>
  public static int[] BuildQuantTable(bool isLuminance, int quality)
    => JpegStandardTables.ScaleQuantTable(
      isLuminance ? JpegStandardTables.LuminanceQuantTable : JpegStandardTables.ChrominanceQuantTable,
      quality
    );

  /// <summary>
  /// How much larger the forward transform's output is than the coefficient it stands for.
  /// </summary>
  /// <remarks>
  /// The integer forward transform used here is libjpeg's slow-but-accurate one, whose output is
  /// deliberately left scaled up by eight so that the division that follows can absorb it. A
  /// quantiser that divides by the table value alone therefore writes coefficients eight times too
  /// large — and since the decoder in this project multiplies by the table value alone as well, the
  /// two agreed with each other and with nobody else. Every JPEG written here was wrong, and no
  /// round trip through this project's own pair could show it.
  /// </remarks>
  public const int ForwardDctScale = 8;

  /// <summary>Quantizes a single coefficient value straight from the forward transform.</summary>
  public static short QuantizeDctOutput(int value, int quantStep)
    => Quantize(value, quantStep * ForwardDctScale);

  /// <summary>Quantizes a single coefficient value.</summary>
  public static short Quantize(int value, int quantStep) {
    if (value >= 0)
      return (short)((value + (quantStep >> 1)) / quantStep);
    return (short)(-((-value + (quantStep >> 1)) / quantStep));
  }

  /// <summary>Dequantizes a single coefficient value.</summary>
  public static int Dequantize(short coefficient, int quantStep) => coefficient * quantStep;
}
