using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SifImage;

/// <summary>In-memory representation of a SIF image.</summary>
public sealed class SifImageFile : IImageFileFormat<SifImageFile> {

  static string IImageFileFormat<SifImageFile>.PrimaryExtension => ".sif";
  static string[] IImageFileFormat<SifImageFile>.FileExtensions => [".sif"];
  static SifImageFile IImageFileFormat<SifImageFile>.FromFile(FileInfo file) => SifImageReader.FromFile(file);
  static SifImageFile IImageFileFormat<SifImageFile>.FromBytes(byte[] data) => SifImageReader.FromBytes(data);
  static SifImageFile IImageFileFormat<SifImageFile>.FromStream(Stream stream) => SifImageReader.FromStream(stream);
  static byte[] IImageFileFormat<SifImageFile>.ToBytes(SifImageFile file) => SifImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "SIF\0" (0x53 0x49 0x46 0x00).</summary>
  internal static readonly byte[] Magic = [0x53, 0x49, 0x46, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this SIF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SifImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Not supported.</summary>
  public static SifImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to SifImageFile is not supported.");
  }
}
