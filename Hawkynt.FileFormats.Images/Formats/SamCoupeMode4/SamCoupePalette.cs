using System;

namespace FileFormat.SamCoupeMode4;

/// <summary>Converts SAM Coupe hardware colour values to RGB.</summary>
/// <remarks>
/// A colour byte carries seven meaningful bits. Each channel is built from a low bit worth 0x49
/// and a high bit worth 0x92, plus one shared brightness bit worth 0x24 that lifts all three —
/// so a fully-set channel reaches 0xFF exactly.
/// </remarks>
public static class SamCoupePalette {

  /// <summary>Number of entries a screen's palette holds.</summary>
  public const int EntryCount = 16;

  private const int _BLUE_LOW = 0x000049, _RED_LOW = 0x490000, _GREEN_LOW = 0x004900;
  private const int _BRIGHT = 0x242424;
  private const int _BLUE_HIGH = 0x000092, _RED_HIGH = 0x920000, _GREEN_HIGH = 0x009200;

  /// <summary>Expands a hardware colour byte to packed 0xRRGGBB.</summary>
  public static int ToRgb(byte value) {
    var rgb = 0;
    if ((value & 1) != 0) rgb |= _BLUE_LOW;
    if ((value & 2) != 0) rgb |= _RED_LOW;
    if ((value & 4) != 0) rgb |= _GREEN_LOW;
    if ((value & 8) != 0) rgb |= _BRIGHT;
    if ((value & 16) != 0) rgb |= _BLUE_HIGH;
    if ((value & 32) != 0) rgb |= _RED_HIGH;
    if ((value & 64) != 0) rgb |= _GREEN_HIGH;
    return rgb;
  }

  /// <summary>Finds the hardware colour byte whose RGB is closest to the given colour.</summary>
  public static byte FromRgb(byte red, byte green, byte blue) {
    var best = (byte)0;
    var bestDistance = int.MaxValue;
    for (var candidate = 0; candidate < 128; ++candidate) {
      var rgb = ToRgb((byte)candidate);
      int dr = ((rgb >> 16) & 0xFF) - red, dg = ((rgb >> 8) & 0xFF) - green, db = (rgb & 0xFF) - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = (byte)candidate;
      if (distance == 0)
        break;
    }

    return best;
  }

  /// <summary>Expands a block of colour bytes into RGB triplets.</summary>
  public static byte[] ToRgbTriplets(ReadOnlySpan<byte> values) {
    var palette = new byte[values.Length * 3];
    for (var i = 0; i < values.Length; ++i) {
      var rgb = ToRgb(values[i]);
      palette[i * 3] = (byte)(rgb >> 16);
      palette[i * 3 + 1] = (byte)(rgb >> 8);
      palette[i * 3 + 2] = (byte)rgb;
    }

    return palette;
  }
}
