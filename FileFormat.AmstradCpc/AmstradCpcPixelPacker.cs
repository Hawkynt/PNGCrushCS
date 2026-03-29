using System;

namespace FileFormat.AmstradCpc;

/// <summary>Packs and unpacks pixel indices to/from CPC byte format for each mode.</summary>
internal static class AmstradCpcPixelPacker {

  /// <summary>Unpacks pixel color indices from a single CPC byte in the given mode.</summary>
  public static byte[] UnpackByte(byte value, AmstradCpcMode mode) => mode switch {
    AmstradCpcMode.Mode0 => _UnpackMode0(value),
    AmstradCpcMode.Mode1 => _UnpackMode1(value),
    AmstradCpcMode.Mode2 => _UnpackMode2(value),
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown CPC mode.")
  };

  /// <summary>Packs pixel color indices into a single CPC byte in the given mode.</summary>
  public static byte PackByte(byte[] pixels, AmstradCpcMode mode) => mode switch {
    AmstradCpcMode.Mode0 => _PackMode0(pixels),
    AmstradCpcMode.Mode1 => _PackMode1(pixels),
    AmstradCpcMode.Mode2 => _PackMode2(pixels),
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown CPC mode.")
  };

  // Mode 0: 2 pixels per byte, 4-bit color indices
  // pixel0 = bits 7,3,5,1 (bit3=MSB, bit1=LSB of a 4-bit value, interleaved)
  // pixel1 = bits 6,2,4,0
  private static byte[] _UnpackMode0(byte value) {
    var p0 = ((value >> 7) & 1)
           | (((value >> 3) & 1) << 1)
           | (((value >> 5) & 1) << 2)
           | (((value >> 1) & 1) << 3);

    var p1 = ((value >> 6) & 1)
           | (((value >> 2) & 1) << 1)
           | (((value >> 4) & 1) << 2)
           | (((value >> 0) & 1) << 3);

    return [(byte)p0, (byte)p1];
  }

  private static byte _PackMode0(byte[] pixels) {
    var p0 = pixels[0];
    var p1 = pixels[1];

    return (byte)(
      (((p0 >> 0) & 1) << 7) |
      (((p0 >> 1) & 1) << 3) |
      (((p0 >> 2) & 1) << 5) |
      (((p0 >> 3) & 1) << 1) |
      (((p1 >> 0) & 1) << 6) |
      (((p1 >> 1) & 1) << 2) |
      (((p1 >> 2) & 1) << 4) |
      (((p1 >> 3) & 1) << 0)
    );
  }

  // Mode 1: 4 pixels per byte, 2-bit color indices
  // pixel0 = bits 7,3; pixel1 = bits 6,2; pixel2 = bits 5,1; pixel3 = bits 4,0
  private static byte[] _UnpackMode1(byte value) {
    var p0 = ((value >> 7) & 1) | (((value >> 3) & 1) << 1);
    var p1 = ((value >> 6) & 1) | (((value >> 2) & 1) << 1);
    var p2 = ((value >> 5) & 1) | (((value >> 1) & 1) << 1);
    var p3 = ((value >> 4) & 1) | (((value >> 0) & 1) << 1);

    return [(byte)p0, (byte)p1, (byte)p2, (byte)p3];
  }

  private static byte _PackMode1(byte[] pixels) {
    var p0 = pixels[0];
    var p1 = pixels[1];
    var p2 = pixels[2];
    var p3 = pixels[3];

    return (byte)(
      (((p0 >> 0) & 1) << 7) |
      (((p0 >> 1) & 1) << 3) |
      (((p1 >> 0) & 1) << 6) |
      (((p1 >> 1) & 1) << 2) |
      (((p2 >> 0) & 1) << 5) |
      (((p2 >> 1) & 1) << 1) |
      (((p3 >> 0) & 1) << 4) |
      (((p3 >> 1) & 1) << 0)
    );
  }

  // Mode 2: 8 pixels per byte, 1-bit color indices, MSB first
  private static byte[] _UnpackMode2(byte value) {
    var pixels = new byte[8];
    for (var i = 0; i < 8; ++i)
      pixels[i] = (byte)((value >> (7 - i)) & 1);

    return pixels;
  }

  private static byte _PackMode2(byte[] pixels) {
    byte result = 0;
    for (var i = 0; i < 8; ++i)
      result |= (byte)((pixels[i] & 1) << (7 - i));

    return result;
  }
}
