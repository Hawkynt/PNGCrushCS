using System;
using FileFormat.Core;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>In-memory representation of a Spooky Sprites Atari Falcon compressed 16-bit true color (.tre) image.</summary>
public readonly record struct SpookySpritesFalconFile : IImageFormatReader<SpookySpritesFalconFile>, IImageToRawImage<SpookySpritesFalconFile>, IImageFromRawImage<SpookySpritesFalconFile>, IImageFormatWriter<SpookySpritesFalconFile> {

  static string IImageFormatMetadata<SpookySpritesFalconFile>.PrimaryExtension => ".tre";
  static string[] IImageFormatMetadata<SpookySpritesFalconFile>.FileExtensions => [".tre"];
  static SpookySpritesFalconFile IImageFormatReader<SpookySpritesFalconFile>.FromSpan(ReadOnlySpan<byte> data) => SpookySpritesFalconReader.FromSpan(data);
  static byte[] IImageFormatWriter<SpookySpritesFalconFile>.ToBytes(SpookySpritesFalconFile file) => SpookySpritesFalconWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, decompressed).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SpookySpritesFalconFile file) {

    var rgb565 = file.PixelData;
    var pixelCount = file.Width * file.Height;
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
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  public static SpookySpritesFalconFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    var rgb24 = image.PixelData;
    var pixelCount = image.Width * image.Height;
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
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = rgb565,
    };
  }
}
