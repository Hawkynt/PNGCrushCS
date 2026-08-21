using System;

namespace FileFormat.Codecs.Hap;

/// <summary>
/// Turns Scaled YCoCg back into RGB, following the reconstruction van Waveren and Castaño give in
/// "Real-Time YCoCg-DXT Compression" (id Software / NVIDIA, September 2007) — the paper the Hap
/// specification names as the definition of this pixel format.
/// </summary>
/// <remarks>
/// A Scaled-YCoCg-DXT5 block is an ordinary DXT5 block read for different meaning: the eight-sample
/// alpha channel, which DXT5 reproduces exactly rather than by three-bit index into a four-point
/// palette, carries luma (Y) at full precision; the DXT1-style colour part carries the chroma pair
/// (Co, Cg) in its red and green channels, each stored as a signed offset from 128, and a per-block
/// scale factor in the blue channel that widens Co and Cg back out before the quantisation to 5 and 6
/// bits crushed them.
/// <para/>
/// The paper's fragment-program pseudocode works in the [0,1] texture space a GPU samples in:
/// <c>scale = color.z * (255/8) + 1</c>, <c>Co = (color.x - 128/255) / scale</c>, <c>Cg = (color.y -
/// 128/255) / scale</c>, <c>Y = color.w</c>, then <c>R = Y + Co - Cg</c>, <c>G = Y + Cg</c>, <c>B = Y -
/// Co - Cg</c>. Every term there is an 8-bit sample divided by 255, and 255 is a common factor of
/// every term on both sides, so the whole thing carries over unchanged into the 0–255 domain this
/// method works in: <c>scale = blue/8 + 1</c>, <c>Co = red - 128</c>, <c>Cg = green - 128</c>, both
/// then divided by <c>scale</c>, and <c>Y</c> the alpha sample itself.
/// <para/>
/// Why the blue channel's scale factor survives DXT1's 5-bit quantisation exactly: the encoder writes
/// it as a multiple of 8, and 5-bit quantisation of an 8-bit value keeps every multiple of 8 exact —
/// dividing by 8 and multiplying back by 8 changes nothing. The scale is meant to hold only 1, 2 or 4,
/// but nothing here assumes that; whatever multiple of 8 the block actually decodes to is used as it
/// is, since a block is free to encode a scale the paper's own algorithm would never choose and a
/// reader has no way to tell that apart from one it would.
/// </remarks>
internal static class HapYCoCgConversion {

  /// <summary>
  /// Converts one pixel already decoded as a raw DXT5 block — R holding Co+128, G holding Cg+128, B
  /// holding the block's scale factor and A holding Y — into RGB.
  /// </summary>
  public static (byte R, byte G, byte B) ToRgb(byte rawRed, byte rawGreen, byte rawBlue, byte y) {
    var co = rawRed - 128;
    var cg = rawGreen - 128;
    var scale = rawBlue / 8.0 + 1.0;

    var co2 = co / scale;
    var cg2 = cg / scale;

    var r = y + co2 - cg2;
    var g = y + cg2;
    var b = y - co2 - cg2;

    return (_Clamp(r), _Clamp(g), _Clamp(b));
  }

  private static byte _Clamp(double value) {
    var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
    return (byte)(rounded < 0 ? 0 : rounded > 255 ? 255 : rounded);
  }
}
