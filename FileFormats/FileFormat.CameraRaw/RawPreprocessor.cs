using System;

namespace FileFormat.CameraRaw;

/// <summary>Pre-processes raw Bayer data before demosaicing: black level subtraction, white balance, linearization to 8-bit.</summary>
internal static class RawPreprocessor {

  /// <summary>Apply black level subtraction and white balance to raw CFA data, producing 8-bit output.</summary>
  /// <param name="raw">Raw sensor data (16-bit per pixel).</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="pattern">Bayer pattern.</param>
  /// <param name="blackLevel">Per-CFA-position black levels [top-left, top-right, bottom-left, bottom-right] or single value.</param>
  /// <param name="whiteLevel">Maximum sensor value (e.g., 4095 for 12-bit, 16383 for 14-bit).</param>
  /// <param name="whiteBalance">Per-channel white balance multipliers [R, G, B], or null for as-shot (unity).</param>
  /// <returns>Preprocessed 8-bit data scaled to 0-255.</returns>
  public static byte[] Process(ushort[] raw, int width, int height, BayerPattern pattern, int[] blackLevel, int whiteLevel, float[]? whiteBalance) {
    var result = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = y * width + x;
        var color = BayerDemosaic.GetColorAt(x, y, pattern);

        var black = blackLevel.Length >= 4
          ? blackLevel[_CfaIndex(x, y)]
          : blackLevel.Length > 0
            ? blackLevel[0]
            : 0;

        var wbMul = whiteBalance != null && color < whiteBalance.Length
          ? whiteBalance[color]
          : 1.0f;

        var value = Math.Max(0, raw[idx] - black);
        var range = Math.Max(1, whiteLevel - black);
        var normalized = value * wbMul / range;
        result[idx] = (byte)Math.Clamp((int)(normalized * 255.0f + 0.5f), 0, 255);
      }

    return result;
  }

  /// <summary>Apply black level subtraction and white balance to 8-bit raw CFA data.</summary>
  /// <param name="raw">Raw sensor data (8-bit per pixel, modified in-place).</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="pattern">Bayer pattern.</param>
  /// <param name="blackLevel">Per-CFA-position black levels or single value.</param>
  /// <param name="whiteLevel">Maximum sensor value (255 for 8-bit).</param>
  /// <param name="whiteBalance">Per-channel white balance multipliers [R, G, B], or null for as-shot (unity).</param>
  /// <returns>Preprocessed 8-bit data scaled to 0-255.</returns>
  public static byte[] Process(byte[] raw, int width, int height, BayerPattern pattern, int[] blackLevel, int whiteLevel, float[]? whiteBalance) {
    var result = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = y * width + x;
        var color = BayerDemosaic.GetColorAt(x, y, pattern);

        var black = blackLevel.Length >= 4
          ? blackLevel[_CfaIndex(x, y)]
          : blackLevel.Length > 0
            ? blackLevel[0]
            : 0;

        var wbMul = whiteBalance != null && color < whiteBalance.Length
          ? whiteBalance[color]
          : 1.0f;

        var value = Math.Max(0, raw[idx] - black);
        var range = Math.Max(1, whiteLevel - black);
        var normalized = value * wbMul / range;
        result[idx] = (byte)Math.Clamp((int)(normalized * 255.0f + 0.5f), 0, 255);
      }

    return result;
  }

  /// <summary>Apply black level subtraction and white balance to 16-bit raw CFA data, keeping 16-bit precision for higher dynamic range.</summary>
  /// <param name="raw">Raw sensor data (16-bit per pixel).</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="pattern">Bayer pattern.</param>
  /// <param name="blackLevel">Per-CFA-position black levels or single value.</param>
  /// <param name="whiteLevel">Maximum sensor value.</param>
  /// <param name="whiteBalance">Per-channel white balance multipliers [R, G, B], or null for as-shot (unity).</param>
  /// <param name="maxValue">Output maximum value (e.g., 65535 for 16-bit, 4095 for 12-bit).</param>
  /// <returns>Preprocessed 16-bit data scaled to [0, maxValue].</returns>
  public static ushort[] ProcessToUInt16(ushort[] raw, int width, int height, BayerPattern pattern, int[] blackLevel, int whiteLevel, float[]? whiteBalance, int maxValue) {
    var result = new ushort[width * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = y * width + x;
        if (idx >= raw.Length)
          break;

        var color = BayerDemosaic.GetColorAt(x, y, pattern);

        var black = blackLevel.Length >= 4
          ? blackLevel[_CfaIndex(x, y)]
          : blackLevel.Length > 0
            ? blackLevel[0]
            : 0;

        var wbMul = whiteBalance != null && color < whiteBalance.Length
          ? whiteBalance[color]
          : 1.0f;

        var value = Math.Max(0, raw[idx] - black);
        var range = Math.Max(1, whiteLevel - black);
        var normalized = value * wbMul / range;
        result[idx] = (ushort)Math.Clamp((int)(normalized * maxValue + 0.5f), 0, maxValue);
      }

    return result;
  }

  /// <summary>Map (x,y) to CFA 2x2 sub-pixel index: 0=top-left, 1=top-right, 2=bottom-left, 3=bottom-right.</summary>
  private static int _CfaIndex(int x, int y) => (y & 1) * 2 + (x & 1);
}
