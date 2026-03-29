using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariGraphics9;

/// <summary>In-memory representation of an Atari Graphics 9 (GTIA 16-shade grayscale) image. 80x192.</summary>
public sealed class AtariGraphics9File : IImageFileFormat<AtariGraphics9File> {

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 80;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline (40 bytes = 80 pixels / 2 pixels per byte).</summary>
  internal const int BytesPerLine = 40;

  /// <summary>Exact file size in bytes (40 bytes/line x 192 lines).</summary>
  internal const int FileSize = BytesPerLine * PixelHeight;

  static string IImageFileFormat<AtariGraphics9File>.PrimaryExtension => ".gr9";
  static string[] IImageFileFormat<AtariGraphics9File>.FileExtensions => [".gr9", ".g9"];
  static FormatCapability IImageFileFormat<AtariGraphics9File>.Capabilities => FormatCapability.IndexedOnly;
  static AtariGraphics9File IImageFileFormat<AtariGraphics9File>.FromFile(FileInfo file) => AtariGraphics9Reader.FromFile(file);
  static AtariGraphics9File IImageFileFormat<AtariGraphics9File>.FromBytes(byte[] data) => AtariGraphics9Reader.FromBytes(data);
  static AtariGraphics9File IImageFileFormat<AtariGraphics9File>.FromStream(Stream stream) => AtariGraphics9Reader.FromStream(stream);
  static RawImage IImageFileFormat<AtariGraphics9File>.ToRawImage(AtariGraphics9File file) => ToRawImage(file);
  static AtariGraphics9File IImageFileFormat<AtariGraphics9File>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AtariGraphics9File>.ToBytes(AtariGraphics9File file) => AtariGraphics9Writer.ToBytes(file);

  /// <summary>Always 80.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw screen data (7680 bytes). Each byte contains 2 pixels in nybbles (upper=left, lower=right), values 0-15.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts the Graphics 9 image to a Gray8 raw image (80x192). Each nybble 0-15 maps to grayscale 0-255 (value x 17).</summary>
  public static RawImage ToRawImage(AtariGraphics9File file) {
    ArgumentNullException.ThrowIfNull(file);

    var gray = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < BytesPerLine; ++x) {
        var srcIndex = y * BytesPerLine + x;
        var b = srcIndex < file.PixelData.Length ? file.PixelData[srcIndex] : (byte)0;

        var leftPixel = (b >> 4) & 0x0F;
        var rightPixel = b & 0x0F;

        var dstIndex = y * PixelWidth + x * 2;
        gray[dstIndex] = (byte)(leftPixel * 17);
        gray[dstIndex + 1] = (byte)(rightPixel * 17);
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Gray8,
      PixelData = gray,
    };
  }

  /// <summary>Creates a Graphics 9 image from a Gray8 raw image (80x192). Quantizes to 16 levels.</summary>
  public static AtariGraphics9File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[FileSize];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < BytesPerLine; ++x) {
        var srcIndex = y * PixelWidth + x * 2;
        var left = image.PixelData[srcIndex] / 17;
        var right = image.PixelData[srcIndex + 1] / 17;

        if (left > 15) left = 15;
        if (right > 15) right = 15;

        pixelData[y * BytesPerLine + x] = (byte)((left << 4) | right);
      }

    return new() { PixelData = pixelData };
  }
}
