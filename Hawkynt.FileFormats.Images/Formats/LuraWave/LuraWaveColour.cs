namespace FileFormat.LuraWave;

/// <summary>Turns decoded channel samples into display pixels for each <see cref="LuraWaveColourMode"/>.</summary>
/// <remarks>
/// The quadratic coefficients of <see cref="LuraWaveColourMode.FourChannel"/> and <see cref="LuraWaveColourMode.Polynomial"/>
/// are the decoder's own constants, copied exactly.
/// </remarks>
internal static class LuraWaveColour {

  private static byte Clamp8(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  private static ushort Clamp16(int value) => value < 0 ? (ushort)0 : value > 65535 ? (ushort)65535 : (ushort)value;

  /// <summary>
  /// Bits to drop from a wide sample to reach eight: the bit length of the maximum (at least 8, at most 16) minus 8.
  /// </summary>
  public static int WideShift(int maxValue) {
    var bits = 16;
    for (var mask = 0x8000; bits > 8 && (maxValue & mask) == 0; mask >>= 1)
      --bits;
    return bits - 8;
  }

  /// <summary>The largest wide sample: the <c>MV</c> chunk's value, or 65535 without one.</summary>
  public static int WideMaximum(LuraWaveHeader header) => header.MaxValue == 0 ? 65535 : header.MaxValue;

  /// <summary>
  /// The maximum and shift behind the 8-bit view of a wide mode. A maximum of exactly 32768 is taken as 32767 there,
  /// as LuraTech's decoder does.
  /// </summary>
  public static (int MaxValue, int Shift) WideRange(LuraWaveHeader header) {
    var maximum = WideMaximum(header);
    if (maximum == 0x8000)
      maximum = 0x7FFF;
    return (maximum, WideShift(maximum));
  }

  /// <summary>One 8-bit grey sample from the first channel at <paramref name="i"/>.</summary>
  public static byte Grey8(LuraWaveColourMode mode, int[] s, int i, int maxValue, int shift) => mode switch {
    LuraWaveColourMode.WideGrey => Clamp8(Math.Clamp(s[i], 0, maxValue) >> shift),
    LuraWaveColourMode.WideSignedGrey => Clamp8(Math.Clamp(s[i] + (maxValue >> 1), 0, maxValue) >> shift),
    _ => Clamp8(s[i] + 128),
  };

  /// <summary>One 16-bit grey sample for the wide grey modes, clamped to the stream's maximum.</summary>
  public static ushort Grey16(LuraWaveColourMode mode, int[] s, int i, int maxValue)
    => (ushort)Math.Clamp(mode == LuraWaveColourMode.WideSignedGrey ? s[i] + (maxValue >> 1) : s[i], 0, maxValue);

  /// <summary>Writes one RGB pixel for the colour modes from the interleaved samples at <paramref name="i"/>.</summary>
  public static void Rgb(LuraWaveColourMode mode, int[] s, int i, int shift, byte[] output, int o) {
    int r, g, b;
    switch (mode) {
      case LuraWaveColourMode.ColourDifference:
        (r, g, b) = FromColourDifference(s[i], s[i + 1], s[i + 2]);
        r += 128;
        g += 128;
        b += 128;
        break;
      case LuraWaveColourMode.WideColourDifference:
        (r, g, b) = FromColourDifference(s[i], s[i + 1], s[i + 2]);
        r >>= shift;
        g >>= shift;
        b >>= shift;
        break;
      case LuraWaveColourMode.Rgb:
        r = s[i] + 128;
        g = s[i + 1] + 128;
        b = s[i + 2] + 128;
        break;
      case LuraWaveColourMode.ReversibleColour:
        (r, g, b) = FromReversible(s[i] + 128, s[i + 1], s[i + 2]);
        break;
      case LuraWaveColourMode.WideReversibleColour:
        (r, g, b) = FromReversible(s[i], s[i + 1], s[i + 2]);
        r >>= shift;
        g >>= shift;
        b >>= shift;
        break;
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

  /// <summary>
  /// The colour-difference model: the first channel is four times a luma that weighs green twice, the others are luma
  /// minus blue and luma minus red. Green is recovered exactly from the full first channel, not from the rounded luma.
  /// </summary>
  private static (int R, int G, int B) FromColourDifference(int y4, int u, int v) {
    var luma = y4 >> 2;
    var red = luma - v;
    var blue = luma - u;
    return (red, (y4 - red - blue) >> 1, blue);
  }

  private static (int R, int G, int B) FromReversible(int y, int u, int v) {
    var g = y - ((u + v) >> 2);
    return (u + g, g, v + g);
  }

  private static (int R, int G, int B) FromFourChannel(int y, int u, int v, int k) {
    var g = y - ((u + v) >> 2);
    return FromFourChannelInput(u + g, g, v + g, k);
  }

  // The quadratic takes the encoder's four input samples: all at 255 come out as paper white, all at 0 as black.
  private static (int R, int G, int B) FromFourChannelInput(int r, int g, int b, int k) {
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

  /// <summary>
  /// Writes one 8-bit RGB pixel for <see cref="LuraWaveColourMode.WideFourChannel"/> from its four channels as
  /// LuraTech's decoder returns them: each is reduced to 8 bits by <paramref name="shift"/> and mapped by the quadratic of
  /// <see cref="LuraWaveColourMode.FourChannel"/>, which is LuraTech's own rendering of the 8-bit counterpart.
  /// </summary>
  public static void RgbFromWideFourChannel(ushort[] c, int i, int shift, byte[] output, int o) {
    var (r, g, b) = FromFourChannelInput(c[i] >> shift, c[i + 1] >> shift, c[i + 2] >> shift, c[i + 3] >> shift);
    output[o] = Clamp8(r);
    output[o + 1] = Clamp8(g);
    output[o + 2] = Clamp8(b);
  }

  /// <summary>
  /// Writes one 8-bit sRGB pixel for <see cref="LuraWaveColourMode.WideLab"/> from its three channels as LuraTech's
  /// decoder returns them.
  /// </summary>
  /// <remarks>
  /// The channels are read as the 8-bit Lab encoding of <see cref="LuraWaveColourMode.Polynomial"/> (L* = 100·L/255,
  /// a* = a − 128) widened by <paramref name="shift"/> bits, so that dropping those bits gives the 8-bit view. At 16
  /// bits this is the ICC version 2 encoding: L* = 100·L/65280, a* = a/256 − 128. The colour is taken relative to a D65
  /// white, the white point LuraTech's own Lab-to-RGB quadratic fits best, and converted to sRGB (IEC 61966-2-1).
  /// </remarks>
  public static void SrgbFromWideLab(ushort[] c, int i, int shift, byte[] output, int o) {
    var unit = (double)(1 << shift);
    var lightness = Math.Min(100.0, c[i] * 100.0 / (255 * unit));
    var a = c[i + 1] / unit - 128;
    var b = c[i + 2] / unit - 128;

    var fy = (lightness + 16) / 116;
    var x = WhiteX * LabInverse(fy + a / 500);
    var y = LabInverse(fy);
    var z = WhiteZ * LabInverse(fy - b / 200);

    // IEC 61966-2-1: XYZ (D65) to linear sRGB.
    output[o] = SrgbByte(3.2406 * x - 1.5372 * y - 0.4986 * z);
    output[o + 1] = SrgbByte(-0.9689 * x + 1.8758 * y + 0.0415 * z);
    output[o + 2] = SrgbByte(0.0557 * x - 0.2040 * y + 1.0570 * z);
  }

  // D65 (x = 0.3127, y = 0.3290), the white point of sRGB, scaled to Y = 1.
  private const double WhiteX = 0.3127 / 0.3290;
  private const double WhiteZ = (1 - 0.3127 - 0.3290) / 0.3290;

  // CIE 15: the inverse of the cube-root compression, linear below (6/29)^3.
  private static double LabInverse(double t) => t > 6.0 / 29 ? t * t * t : 3 * (6.0 / 29) * (6.0 / 29) * (t - 4.0 / 29);

  // IEC 61966-2-1 transfer function, rounded to 8 bits.
  private static byte SrgbByte(double linear) {
    if (!(linear > 0))
      return 0;
    if (linear >= 1)
      return 255;

    var encoded = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
    return (byte)Math.Round(encoded * 255);
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
