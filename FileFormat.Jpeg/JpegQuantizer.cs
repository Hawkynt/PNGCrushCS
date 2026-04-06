namespace FileFormat.Jpeg;

/// <summary>Quantization/dequantization helpers and IJG quality scaling.</summary>
internal static class JpegQuantizer {

  /// <summary>Builds a quantization table for encoding at the given quality (1-100).</summary>
  public static int[] BuildQuantTable(bool isLuminance, int quality)
    => JpegStandardTables.ScaleQuantTable(
      isLuminance ? JpegStandardTables.LuminanceQuantTable : JpegStandardTables.ChrominanceQuantTable,
      quality
    );

  /// <summary>Quantizes a single coefficient value.</summary>
  public static short Quantize(int value, int quantStep) {
    if (value >= 0)
      return (short)((value + (quantStep >> 1)) / quantStep);
    return (short)(-((-value + (quantStep >> 1)) / quantStep));
  }

  /// <summary>Dequantizes a single coefficient value.</summary>
  public static int Dequantize(short coefficient, int quantStep) => coefficient * quantStep;
}
