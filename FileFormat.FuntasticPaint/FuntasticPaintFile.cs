using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FuntasticPaint;

/// <summary>In-memory representation of a Fun*tastic Paint (Atari 8-bit GTIA 16-shade) image (80x192).</summary>
public sealed class FuntasticPaintFile : IImageFileFormat<FuntasticPaintFile> {

  /// <summary>The exact file size: 40 bytes/line x 192 lines.</summary>
  public const int ExpectedFileSize = 7680;

  /// <summary>The fixed width in pixels.</summary>
  public const int FixedWidth = 80;

  /// <summary>The fixed height in pixels.</summary>
  public const int FixedHeight = 192;

  /// <summary>Bytes per scanline (2 pixels per byte, 80/2 = 40).</summary>
  internal const int BytesPerRow = 40;

  static string IImageFileFormat<FuntasticPaintFile>.PrimaryExtension => ".fun8";
  static string[] IImageFileFormat<FuntasticPaintFile>.FileExtensions => [".fun8", ".ftp"];
  static FormatCapability IImageFileFormat<FuntasticPaintFile>.Capabilities => FormatCapability.IndexedOnly;
  static FuntasticPaintFile IImageFileFormat<FuntasticPaintFile>.FromFile(FileInfo file) => FuntasticPaintReader.FromFile(file);
  static FuntasticPaintFile IImageFileFormat<FuntasticPaintFile>.FromBytes(byte[] data) => FuntasticPaintReader.FromBytes(data);
  static FuntasticPaintFile IImageFileFormat<FuntasticPaintFile>.FromStream(Stream stream) => FuntasticPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<FuntasticPaintFile>.ToBytes(FuntasticPaintFile file) => FuntasticPaintWriter.ToBytes(file);

  /// <summary>Always 80.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw pixel data (7680 bytes, 2 pixels per byte as high/low nybbles, 40 bytes per row, 192 rows).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this image to a platform-independent <see cref="RawImage"/> in Gray8 format.</summary>
  public static RawImage ToRawImage(FuntasticPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var gray = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var byteIndex = y * BytesPerRow + x / 2;
        int shade;
        if ((x & 1) == 0)
          shade = (file.PixelData[byteIndex] >> 4) & 0x0F;
        else
          shade = file.PixelData[byteIndex] & 0x0F;

        gray[y * FixedWidth + x] = (byte)(shade * 17);
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Gray8,
      PixelData = gray,
    };
  }

  /// <summary>Creates a Fun*tastic Paint file from a platform-independent <see cref="RawImage"/>.</summary>
  public static FuntasticPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[ExpectedFileSize];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var shade = image.PixelData[y * FixedWidth + x] / 17;
        if (shade > 15)
          shade = 15;

        var byteIndex = y * BytesPerRow + x / 2;
        if ((x & 1) == 0)
          pixelData[byteIndex] |= (byte)(shade << 4);
        else
          pixelData[byteIndex] |= (byte)shade;
      }

    return new() { PixelData = pixelData };
  }
}
