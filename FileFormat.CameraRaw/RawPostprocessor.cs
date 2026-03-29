using System;

namespace FileFormat.CameraRaw;

/// <summary>Post-processes demosaiced RGB data: color matrix application and sRGB gamma correction.</summary>
internal static class RawPostprocessor {

  /// <summary>Apply camera-to-sRGB color matrix and gamma correction.</summary>
  /// <param name="rgb">RGB24 pixel data (modified in place).</param>
  /// <param name="colorMatrix">3x3 camera-to-sRGB matrix (row-major, 9 floats), or null for identity.</param>
  public static void Process(byte[] rgb, float[]? colorMatrix) {
    if (colorMatrix == null || colorMatrix.Length < 9)
      colorMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    for (var i = 0; i < rgb.Length; i += 3) {
      var r = rgb[i] / 255.0f;
      var g = rgb[i + 1] / 255.0f;
      var b = rgb[i + 2] / 255.0f;

      var rr = r * colorMatrix[0] + g * colorMatrix[1] + b * colorMatrix[2];
      var gg = r * colorMatrix[3] + g * colorMatrix[4] + b * colorMatrix[5];
      var bb = r * colorMatrix[6] + g * colorMatrix[7] + b * colorMatrix[8];

      rgb[i] = _ToSrgb(rr);
      rgb[i + 1] = _ToSrgb(gg);
      rgb[i + 2] = _ToSrgb(bb);
    }
  }

  /// <summary>Convert linear-light value to sRGB gamma-encoded byte.</summary>
  private static byte _ToSrgb(float linear) {
    linear = Math.Clamp(linear, 0f, 1f);
    var srgb = linear <= 0.0031308f
      ? linear * 12.92f
      : 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
    return (byte)Math.Clamp((int)(srgb * 255.0f + 0.5f), 0, 255);
  }
}
