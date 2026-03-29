using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.QdvImage;

/// <summary>In-memory representation of a QDV image.</summary>
public sealed class QdvImageFile : IImageFileFormat<QdvImageFile> {

  static string IImageFileFormat<QdvImageFile>.PrimaryExtension => ".qdv";
  static string[] IImageFileFormat<QdvImageFile>.FileExtensions => [".qdv"];
  static QdvImageFile IImageFileFormat<QdvImageFile>.FromFile(FileInfo file) => QdvImageReader.FromFile(file);
  static QdvImageFile IImageFileFormat<QdvImageFile>.FromBytes(byte[] data) => QdvImageReader.FromBytes(data);
  static QdvImageFile IImageFileFormat<QdvImageFile>.FromStream(Stream stream) => QdvImageReader.FromStream(stream);
  static byte[] IImageFileFormat<QdvImageFile>.ToBytes(QdvImageFile file) => QdvImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "QDV\0" (0x51 0x44 0x56 0x00).</summary>
  internal static readonly byte[] Magic = [0x51, 0x44, 0x56, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) + flags(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this QDV image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(QdvImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Not supported.</summary>
  public static QdvImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to QdvImageFile is not supported.");
  }
}
