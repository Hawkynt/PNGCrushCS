using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FaxMan;

/// <summary>In-memory representation of a FaxMan FMF image.</summary>
public sealed class FaxManFile : IImageFileFormat<FaxManFile> {

  static string IImageFileFormat<FaxManFile>.PrimaryExtension => ".fmf";
  static string[] IImageFileFormat<FaxManFile>.FileExtensions => [".fmf"];
  static FaxManFile IImageFileFormat<FaxManFile>.FromFile(FileInfo file) => FaxManReader.FromFile(file);
  static FaxManFile IImageFileFormat<FaxManFile>.FromBytes(byte[] data) => FaxManReader.FromBytes(data);
  static FaxManFile IImageFileFormat<FaxManFile>.FromStream(Stream stream) => FaxManReader.FromStream(stream);
  static byte[] IImageFileFormat<FaxManFile>.ToBytes(FaxManFile file) => FaxManWriter.ToBytes(file);

  /// <summary>Magic bytes: "FM" (0x46 0x4D).</summary>
  internal static readonly byte[] Magic = [0x46, 0x4D];

  /// <summary>Header size: magic(2) + width(2) + height(2) + version(2) + flags(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this FMF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FaxManFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerRow = (file.Width + 7) / 8;
    var rgb = new byte[file.Width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var byteIndex = y * bytesPerRow + x / 8;
        var bitIndex = 7 - (x % 8);
        var bit = (file.PixelData[byteIndex] >> bitIndex) & 1;
        var offset = (y * file.Width + x) * 3;
        var color = bit == 1 ? (byte)0 : (byte)255;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported.</summary>
  public static FaxManFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to FaxManFile is not supported.");
  }
}
