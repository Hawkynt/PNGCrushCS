using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SeqImage;

/// <summary>In-memory representation of a SEQ image.</summary>
public sealed class SeqImageFile : IImageFileFormat<SeqImageFile> {

  static string IImageFileFormat<SeqImageFile>.PrimaryExtension => ".seq";
  static string[] IImageFileFormat<SeqImageFile>.FileExtensions => [".seq"];
  static SeqImageFile IImageFileFormat<SeqImageFile>.FromFile(FileInfo file) => SeqImageReader.FromFile(file);
  static SeqImageFile IImageFileFormat<SeqImageFile>.FromBytes(byte[] data) => SeqImageReader.FromBytes(data);
  static SeqImageFile IImageFileFormat<SeqImageFile>.FromStream(Stream stream) => SeqImageReader.FromStream(stream);
  static byte[] IImageFileFormat<SeqImageFile>.ToBytes(SeqImageFile file) => SeqImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "SEQ\0" (0x53 0x45 0x51 0x00).</summary>
  internal static readonly byte[] Magic = [0x53, 0x45, 0x51, 0x00];

  /// <summary>Header size: magic(4) + version(2) + width(2) + height(2) + frameCount(2) + bpp(2) + reserved(2) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Number of frames.</summary>
  public ushort FrameCount { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this SEQ image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SeqImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Not supported.</summary>
  public static SeqImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to SeqImageFile is not supported.");
  }
}
