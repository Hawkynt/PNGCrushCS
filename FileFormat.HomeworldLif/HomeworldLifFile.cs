using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HomeworldLif;

/// <summary>In-memory representation of a Homeworld LIF texture image.</summary>
public sealed class HomeworldLifFile : IImageFileFormat<HomeworldLifFile> {

  static string IImageFileFormat<HomeworldLifFile>.PrimaryExtension => ".lif";
  static string[] IImageFileFormat<HomeworldLifFile>.FileExtensions => [".lif"];
  static HomeworldLifFile IImageFileFormat<HomeworldLifFile>.FromFile(FileInfo file) => HomeworldLifReader.FromFile(file);
  static HomeworldLifFile IImageFileFormat<HomeworldLifFile>.FromBytes(byte[] data) => HomeworldLifReader.FromBytes(data);
  static HomeworldLifFile IImageFileFormat<HomeworldLifFile>.FromStream(Stream stream) => HomeworldLifReader.FromStream(stream);
  static byte[] IImageFileFormat<HomeworldLifFile>.ToBytes(HomeworldLifFile file) => HomeworldLifWriter.ToBytes(file);

  /// <summary>Magic bytes: "Lif " (0x4C 0x69 0x66 0x20).</summary>
  internal static readonly byte[] Magic = [0x4C, 0x69, 0x66, 0x20];

  /// <summary>Header size: magic(4) + version(4) + width(4) + height(4) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public int Version { get; init; }

  /// <summary>Raw RGBA32 pixel data (4 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this LIF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HomeworldLifFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 4;
      var dstOffset = i * 3;
      rgb[dstOffset] = file.PixelData[srcOffset];
      rgb[dstOffset + 1] = file.PixelData[srcOffset + 1];
      rgb[dstOffset + 2] = file.PixelData[srcOffset + 2];
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported.</summary>
  public static HomeworldLifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to HomeworldLifFile is not supported.");
  }
}
