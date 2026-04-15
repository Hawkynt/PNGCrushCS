using System;
using FileFormat.Core;

namespace EndToEnd.Tests;

/// <summary>Generates canonical RawImage test patterns for end-to-end validation.</summary>
internal static class TestImageFactory {

  /// <summary>2x2 RGBA: red, green, blue, white — catches channel swap bugs immediately.</summary>
  internal static RawImage RedGreenBlueWhite_2x2() => new() {
    Width = 2, Height = 2, Format = PixelFormat.Rgba32,
    PixelData = [
      255, 0, 0, 255,    0, 255, 0, 255,   // row 0: red, green
      0, 0, 255, 255,    255, 255, 255, 255  // row 1: blue, white
    ]
  };

  /// <summary>8x8 gradient where every pixel is unique — catches stride and offset bugs.</summary>
  internal static RawImage Gradient_8x8() {
    var data = new byte[8 * 8 * 4];
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var i = (y * 8 + x) * 4;
        data[i] = (byte)(x * 32);
        data[i + 1] = (byte)(y * 32);
        data[i + 2] = (byte)((x + y) * 16);
        data[i + 3] = 255;
      }
    return new() { Width = 8, Height = 8, Format = PixelFormat.Rgba32, PixelData = data };
  }

  /// <summary>64x64 random — exercises SIMD vector boundary handling (multiple Vector256 chunks).</summary>
  internal static RawImage Random_64x64() {
    var rng = new Random(42); // deterministic seed
    var data = new byte[64 * 64 * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)rng.Next(256);
      data[i + 1] = (byte)rng.Next(256);
      data[i + 2] = (byte)rng.Next(256);
      data[i + 3] = 255;
    }
    return new() { Width = 64, Height = 64, Format = PixelFormat.Rgba32, PixelData = data };
  }

  /// <summary>9 pixels — exactly 36 bytes for 4bpp, forces Vector256 (32 bytes) + 4 scalar bytes. Catches cross-lane bugs.</summary>
  internal static RawImage NinePixels_9x1() {
    var data = new byte[9 * 4];
    for (var i = 0; i < 9; ++i) {
      data[i * 4] = (byte)(i * 28);        // R: 0, 28, 56, 84, ...
      data[i * 4 + 1] = (byte)(255 - i * 28); // G
      data[i * 4 + 2] = (byte)(i * 14);    // B
      data[i * 4 + 3] = 255;
    }
    return new() { Width = 9, Height = 1, Format = PixelFormat.Rgba32, PixelData = data };
  }

  /// <summary>Converts a Rgba32 RawImage to the specified format via PixelConverter.</summary>
  internal static RawImage ConvertTo(RawImage source, PixelFormat target)
    => target == source.Format ? source : PixelConverter.Convert(source, target);
}
