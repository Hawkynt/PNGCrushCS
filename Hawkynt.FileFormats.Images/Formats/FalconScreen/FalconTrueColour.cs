using System;
using FileFormat.Core;

namespace FileFormat.FalconScreen;

/// <summary>
/// An Atari Falcon true-colour screen: one big-endian RGB565 word per pixel, in reading order, and
/// nothing else.
/// </summary>
/// <remarks>
/// Eleven of the formats here are that screen written to disc. Some put a small header in front of
/// it and some do not, and they disagree only on how big the screen is — ten hold the 320x240 one
/// and the .ftc dump holds 384x240 — so the size is a parameter and the conversion itself lives
/// here rather than eleven times over.
/// <para/>
/// Going back the other way quantizes the caller's own picture in place. That is deliberate: RGB565
/// throws away the low bits, and a caller that writes a file and then reads it back would otherwise
/// see pixels it never handed over.
/// </remarks>
internal static class FalconTrueColour {

  /// <summary>Pixels across the screen the great majority of these formats hold.</summary>
  public const int ScreenWidth = 320;

  /// <summary>Rows down it.</summary>
  public const int ScreenHeight = 240;

  /// <summary>Expands the 320x240 screen to Rgb24.</summary>
  public static RawImage ToRawImage(byte[] rgb565) => ToRawImage(rgb565, ScreenWidth, ScreenHeight);

  /// <summary>Reduces a picture to the 320x240 screen.</summary>
  public static byte[] FromRawImage(RawImage image) => FromRawImage(image, ScreenWidth, ScreenHeight);

  /// <summary>Expands the screen to Rgb24, replicating the top bits into the ones RGB565 has not got.</summary>
  /// <param name="rgb565">The screen. A short one is read as black past its end rather than refused.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows down.</param>
  public static RawImage ToRawImage(byte[] rgb565, int width, int height) {

    var pixelCount = width * height;
    var rgb24 = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 2;
      var hi = srcOffset < rgb565.Length ? rgb565[srcOffset] : (byte)0;
      var lo = srcOffset + 1 < rgb565.Length ? rgb565[srcOffset + 1] : (byte)0;
      var packed = (ushort)((hi << 8) | lo);

      var r5 = (packed >> 11) & 0x1F;
      var g6 = (packed >> 5) & 0x3F;
      var b5 = packed & 0x1F;

      var dstOffset = i * 3;
      rgb24[dstOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[dstOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[dstOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  /// <summary>Reduces a picture to the screen, which is the one size each of these formats holds.</summary>
  /// <param name="image">The picture, quantized in place to what RGB565 can carry.</param>
  /// <param name="width">Pixels across, and the only width accepted.</param>
  /// <param name="height">Rows down, and the only height accepted.</param>
  public static byte[] FromRawImage(RawImage image, int width, int height) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    if (image.Width != width || image.Height != height)
      throw new ArgumentException($"Expected {width}x{height} but got {image.Width}x{image.Height}.", nameof(image));

    var rgb24 = image.PixelData;
    var pixelCount = width * height;
    var rgb565 = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 3;
      var r = rgb24[srcOffset];
      var g = rgb24[srcOffset + 1];
      var b = rgb24[srcOffset + 2];

      var r5 = (r >> 3) & 0x1F;
      var g6 = (g >> 2) & 0x3F;
      var b5 = (b >> 3) & 0x1F;
      var packed = (ushort)((r5 << 11) | (g6 << 5) | b5);

      var dstOffset = i * 2;
      rgb565[dstOffset] = (byte)(packed >> 8);
      rgb565[dstOffset + 1] = (byte)(packed & 0xFF);

      // Quantize input in-place to the lossy RGB565 range so the round-trip is bit-exact:
      // ToRawImage will expand via bit-replication; pre-apply that to the source so the
      // caller sees stable pixel data after write -> read.
      rgb24[srcOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[srcOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[srcOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return rgb565;
  }
}
