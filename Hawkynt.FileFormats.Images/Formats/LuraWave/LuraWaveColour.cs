namespace FileFormat.LuraWave;

/// <summary>Turns decoded channel samples into display pixels for each <see cref="LuraWaveColourMode"/>.</summary>
/// <remarks>
/// The quadratic coefficients of <see cref="LuraWaveColourMode.FourChannel"/> and <see cref="LuraWaveColourMode.Polynomial"/>
/// are the decoder's own constants, copied exactly.
/// </remarks>
internal static class LuraWaveColour {

  private static byte Clamp8(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  private static ushort Clamp16(int value) => value < 0 ? (ushort)0 : value > 65535 ? (ushort)65535 : (ushort)value;

  /// <summary>Bits to drop from a wide sample to reach eight; the decoder halves the announced maximum until it fits in 256.</summary>
  public static int WideShift(int maxValue) {
    var shift = 0;
    for (var range = maxValue; range > 256; range >>= 1)
      ++shift;
    return shift;
  }

  /// <summary>
  /// Wide grey modes fall back to a 16-bit maximum when no <c>MV</c> chunk says otherwise; the wide colour mode does not,
  /// and so keeps its samples unshifted.
  /// </summary>
  public static (int MaxValue, int Shift) WideRange(LuraWaveHeader header)
    => header.MaxValue == 0 && header.Mode != LuraWaveColourMode.WideReversibleColour
      ? (65535, 8)
      : (header.MaxValue, WideShift(header.MaxValue));

  /// <summary>One 8-bit grey sample from the first channel at <paramref name="i"/>.</summary>
  public static byte Grey8(LuraWaveColourMode mode, int[] s, int i, int maxValue, int shift) => mode switch {
    LuraWaveColourMode.WideGrey => Clamp8(s[i] >> shift),
    LuraWaveColourMode.WideSignedGrey => Clamp8((s[i] + (maxValue >> 1)) >> shift),
    _ => Clamp8(s[i] + 128),
  };

  /// <summary>One 16-bit grey sample for the wide grey modes.</summary>
  public static ushort Grey16(LuraWaveColourMode mode, int[] s, int i, int maxValue)
    => mode == LuraWaveColourMode.WideSignedGrey ? Clamp16(s[i] + (maxValue >> 1)) : Clamp16(s[i]);

  /// <summary>Writes one RGB pixel for the colour modes from the interleaved samples at <paramref name="i"/>.</summary>
  public static void Rgb(LuraWaveColourMode mode, int[] s, int i, int shift, byte[] output, int o) {
    int r, g, b;
    switch (mode) {
      case LuraWaveColourMode.ColourDifference: {
        var luma = (s[i] >> 2) + 128;
        r = luma - s[i + 2];
        g = luma + ((s[i + 1] + s[i + 2]) >> 1);
        b = luma - s[i + 1];
        break;
      }
      case LuraWaveColourMode.Rgb:
        r = s[i] + 128;
        g = s[i + 1] + 128;
        b = s[i + 2] + 128;
        break;
      case LuraWaveColourMode.ReversibleColour:
        (r, g, b) = FromReversible(s[i] + 128, s[i + 1], s[i + 2]);
        break;
      case LuraWaveColourMode.WideReversibleColour: {
        var y = s[i] >> shift;
        (r, g, b) = FromReversible(y, s[i + 1] >> shift, s[i + 2] >> shift);
        break;
      }
      case LuraWaveColourMode.FourChannel:
        (r, g, b) = FromFourChannel(s[i] + 128, s[i + 1], s[i + 2], s[i + 3] + 128);
        break;
      case LuraWaveColourMode.Polynomial:
        (r, g, b) = FromPolynomial(s[i] + 128, s[i + 1], s[i + 2]);
        break;
      default:
        r = g = b = 0;
        break;
    }

    output[o] = Clamp8(r);
    output[o + 1] = Clamp8(g);
    output[o + 2] = Clamp8(b);
  }

  private static (int R, int G, int B) FromReversible(int y, int u, int v) {
    var g = y - ((u + v) >> 2);
    return (u + g, g, v + g);
  }

  private static (int R, int G, int B) FromFourChannel(int y, int u, int v, int k) {
    var g = y - ((u + v) >> 2);
    var r = u + g;
    var b = v + g;
    var rr = r * r >> 4;
    var gg = g * g >> 4;
    var bb = b * b >> 4;
    var rg = r * g >> 4;
    var rk = r * k >> 4;
    var rb = r * b >> 4;
    var kg = k * g >> 4;
    var kb = k * b >> 4;
    var gb = g * b >> 4;
    var red = -52830 * rr + 24977 * rg + 57294 * rk + -12674 * kg + 20552 * kb + -17889 * gb
      + (51983 * r << 4) + (-12151 * b << 4) >> 20;
    var green = -12660 * rr + -27110 * gg + 9223 * rk + 49218 * kg + 15005 * kb
      + (14458 * r << 4) + (22478 * g << 4) + (-10849 * b << 4) >> 20;
    var blue = -24695 * bb + -16732 * rb + 13983 * kg + 55580 * kb + 9221 * gb + (23589 * b << 4) >> 20;
    return (red, green, blue);
  }

  private static (int R, int G, int B) FromPolynomial(int l, int a, int b) {
    l = l > 255 ? 255 : l < 0 ? 0 : l;
    a = a > 127 ? 127 : a < -128 ? -128 : a;
    b = b > 127 ? 127 : b < -128 ? -128 : b;
    var aa = a * a;
    var bb = b * b;
    var ll = l * l;
    var al = a * l;
    var bl = b * l;
    var ab = a * b;
    var red = (20104 * ll >> 2) + -16115 * aa + -25325 * bb + (25846 * al >> 1) + (8469 * bl >> 1) + -27977 * ab
      + (41231 * a << 7) + (23799 * b << 7) + (46573 * l << 6) >> 22;
    var green = -13522 * aa + 6650 * bb + (-21507 * al >> 1) + (62429 * l << 6) >> 22;
    var blue = (-48443 * b << 1) + 62757 * l >> 16;
    return (red, green, blue);
  }
}
