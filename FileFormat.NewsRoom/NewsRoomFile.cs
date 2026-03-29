using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NewsRoom;

/// <summary>In-memory representation of a NewsRoom NSR image (320x192, 1bpp monochrome).</summary>
public sealed class NewsRoomFile : IImageFileFormat<NewsRoomFile> {

  static string IImageFileFormat<NewsRoomFile>.PrimaryExtension => ".nsr";
  static string[] IImageFileFormat<NewsRoomFile>.FileExtensions => [".nsr"];
  static FormatCapability IImageFileFormat<NewsRoomFile>.Capabilities => FormatCapability.MonochromeOnly;
  static NewsRoomFile IImageFileFormat<NewsRoomFile>.FromFile(FileInfo file) => NewsRoomReader.FromFile(file);
  static NewsRoomFile IImageFileFormat<NewsRoomFile>.FromBytes(byte[] data) => NewsRoomReader.FromBytes(data);
  static NewsRoomFile IImageFileFormat<NewsRoomFile>.FromStream(Stream stream) => NewsRoomReader.FromStream(stream);
  static byte[] IImageFileFormat<NewsRoomFile>.ToBytes(NewsRoomFile file) => NewsRoomWriter.ToBytes(file);

  /// <summary>Fixed image width: 320 pixels.</summary>
  internal const int FixedWidth = 320;

  /// <summary>Fixed image height: 192 pixels.</summary>
  internal const int FixedHeight = 192;

  /// <summary>Bytes per row: 320/8 = 40.</summary>
  internal const int BytesPerRow = FixedWidth / 8;

  /// <summary>Fixed total file size: 40 * 192 = 7680 bytes.</summary>
  internal const int ExpectedFileSize = BytesPerRow * FixedHeight;

  /// <summary>Minimum valid file size (exact match required).</summary>
  public const int MinFileSize = ExpectedFileSize;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw 1bpp bitmap data (7680 bytes, MSB first).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this NewsRoom image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(NewsRoomFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[FixedWidth * FixedHeight * 3];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var byteIndex = y * BytesPerRow + x / 8;
        var bitIndex = 7 - (x % 8);
        var bit = (file.PixelData[byteIndex] >> bitIndex) & 1;
        var offset = (y * FixedWidth + x) * 3;
        var color = bit == 1 ? (byte)0 : (byte)255;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported.</summary>
  public static NewsRoomFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to NewsRoomFile is not supported.");
  }
}
