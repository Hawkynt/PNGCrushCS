using System;

namespace FileFormat.Hdr;

/// <summary>Encodes and decodes RGBE (Radiance) pixel values.</summary>
internal static class RgbeCodec {

  public static (byte R, byte G, byte B, byte E) EncodePixel(float r, float g, float b) {
    var maxComponent = Math.Max(r, Math.Max(g, b));
    if (maxComponent < 1e-32f)
      return (0, 0, 0, 0);

    var exponent = _Ilogb(maxComponent) + 1;
    var mantissa = _Ldexp(1.0f, -exponent + 8);
    return (
      (byte)Math.Clamp(r * mantissa, 0f, 255f),
      (byte)Math.Clamp(g * mantissa, 0f, 255f),
      (byte)Math.Clamp(b * mantissa, 0f, 255f),
      (byte)(exponent + 128)
    );
  }

  public static (float R, float G, float B) DecodePixel(byte r, byte g, byte b, byte e) {
    if (e == 0)
      return (0f, 0f, 0f);

    var factor = _Ldexp(1.0f, e - (128 + 8));
    return (
      (r + 0.5f) * factor,
      (g + 0.5f) * factor,
      (b + 0.5f) * factor
    );
  }

  private static int _Ilogb(float value) {
    if (value <= 0f)
      return -1;

    return (int)Math.Floor(Math.Log2(value));
  }

  private static float _Ldexp(float value, int exponent) => value * MathF.Pow(2f, exponent);
}
