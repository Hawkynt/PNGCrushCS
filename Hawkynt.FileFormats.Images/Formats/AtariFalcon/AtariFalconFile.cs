using System;
using FileFormat.Core;

namespace FileFormat.AtariFalcon;

/// <summary>In-memory representation of an Atari Falcon true-color (.ftc) screen dump.</summary>
public readonly record struct AtariFalconFile : IImageFormatReader<AtariFalconFile>, IImageToRawImage<AtariFalconFile>, IImageFromRawImage<AtariFalconFile>, IImageFormatWriter<AtariFalconFile> {

  /// <summary>Pixels across. Not 320 — that is the size the other Falcon dump here holds.</summary>
  /// <remarks>
  /// This had 320 by 240, which is a real Falcon screen and a real format, but the one filed under
  /// a different extension. A picture of this format is wider, and a file of ours was 30720 bytes
  /// short of one.
  /// </remarks>
  public const int PixelWidth = 384;

  /// <summary>Rows.</summary>
  public const int PixelHeight = 240;

  /// <summary>The exact file size: two bytes a pixel, and nothing else in the file.</summary>
  public const int ExpectedFileSize = PixelWidth * PixelHeight * 2;

  static string IImageFormatMetadata<AtariFalconFile>.PrimaryExtension => ".ftc";
  static string[] IImageFormatMetadata<AtariFalconFile>.FileExtensions => [".ftc"];
  static AtariFalconFile IImageFormatReader<AtariFalconFile>.FromSpan(ReadOnlySpan<byte> data) => AtariFalconReader.FromSpan(data);

  /// <summary>The one size this format holds, which its writer accepts and no other.</summary>
  static VideoMode[] IImageFormatMetadata<AtariFalconFile>.VideoModes => [
    new("Default", [(PixelWidth, PixelHeight)]),
  ];
  static byte[] IImageFormatWriter<AtariFalconFile>.ToBytes(AtariFalconFile file) => AtariFalconWriter.ToBytes(file);

  /// <summary>Always 384.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 240.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AtariFalconFile file) {

    var rgb565 = file.PixelData;
    var pixelCount = PixelWidth * PixelHeight;
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
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  public static AtariFalconFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rgb24 = image.PixelData;
    var pixelCount = PixelWidth * PixelHeight;
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

      // Quantize input in-place to the lossy RGB565 range so the round-trip is bit-exact.
      rgb24[srcOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[srcOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[srcOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      PixelData = rgb565,
    };
  }
}
