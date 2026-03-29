using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.GodPaint;

/// <summary>In-memory representation of a GodPaint (.gpn) screen dump.</summary>
public sealed class GodPaintFile : IImageFileFormat<GodPaintFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  public const int ExpectedFileSize = 320 * 240 * 2;

  static string IImageFileFormat<GodPaintFile>.PrimaryExtension => ".gpn";
  static string[] IImageFileFormat<GodPaintFile>.FileExtensions => [".gpn", ".gdp"];
  static GodPaintFile IImageFileFormat<GodPaintFile>.FromFile(FileInfo file) => GodPaintReader.FromFile(file);
  static GodPaintFile IImageFileFormat<GodPaintFile>.FromBytes(byte[] data) => GodPaintReader.FromBytes(data);
  static GodPaintFile IImageFileFormat<GodPaintFile>.FromStream(Stream stream) => GodPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<GodPaintFile>.ToBytes(GodPaintFile file) => GodPaintWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(GodPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb565 = file.PixelData;
    var pixelCount = 320 * 240;
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
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  public static GodPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));
    if (image.Width != 320 || image.Height != 240)
      throw new ArgumentException($"Expected 320x240 but got {image.Width}x{image.Height}.", nameof(image));

    var rgb24 = image.PixelData;
    var pixelCount = 320 * 240;
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
      PixelData = rgb565,
    };
  }
}
