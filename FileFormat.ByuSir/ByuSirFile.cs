using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ByuSir;

/// <summary>In-memory representation of a BYU SIR grayscale image.</summary>
public sealed class ByuSirFile : IImageFileFormat<ByuSirFile> {

  static string IImageFileFormat<ByuSirFile>.PrimaryExtension => ".sir";
  static string[] IImageFileFormat<ByuSirFile>.FileExtensions => [".sir"];
  static ByuSirFile IImageFileFormat<ByuSirFile>.FromFile(FileInfo file) => ByuSirReader.FromFile(file);
  static ByuSirFile IImageFileFormat<ByuSirFile>.FromBytes(byte[] data) => ByuSirReader.FromBytes(data);
  static ByuSirFile IImageFileFormat<ByuSirFile>.FromStream(Stream stream) => ByuSirReader.FromStream(stream);
  static byte[] IImageFileFormat<ByuSirFile>.ToBytes(ByuSirFile file) => ByuSirWriter.ToBytes(file);

  /// <summary>Magic bytes: "SIR\0" (0x53 0x49 0x52 0x00).</summary>
  internal static readonly byte[] Magic = [0x53, 0x49, 0x52, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) + dataType(2) + reserved(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Data type identifier.</summary>
  public ushort DataType { get; init; }

  /// <summary>Reserved field.</summary>
  public ushort Reserved { get; init; }

  /// <summary>Raw grayscale pixel data (1 byte per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(ByuSirFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    };
  }

  public static ByuSirFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Gray8)
      throw new ArgumentException($"Expected {PixelFormat.Gray8} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      DataType = 1,
      PixelData = image.PixelData[..],
    };
  }
}
