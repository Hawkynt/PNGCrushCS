using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.QuantelVpb;

/// <summary>In-memory representation of a Quantel VPB RGB image.</summary>
public sealed class QuantelVpbFile : IImageFileFormat<QuantelVpbFile> {

  static string IImageFileFormat<QuantelVpbFile>.PrimaryExtension => ".vpb";
  static string[] IImageFileFormat<QuantelVpbFile>.FileExtensions => [".vpb"];
  static QuantelVpbFile IImageFileFormat<QuantelVpbFile>.FromFile(FileInfo file) => QuantelVpbReader.FromFile(file);
  static QuantelVpbFile IImageFileFormat<QuantelVpbFile>.FromBytes(byte[] data) => QuantelVpbReader.FromBytes(data);
  static QuantelVpbFile IImageFileFormat<QuantelVpbFile>.FromStream(Stream stream) => QuantelVpbReader.FromStream(stream);
  static byte[] IImageFileFormat<QuantelVpbFile>.ToBytes(QuantelVpbFile file) => QuantelVpbWriter.ToBytes(file);

  /// <summary>Magic bytes: "QVPB" (0x51 0x56 0x50 0x42).</summary>
  internal static readonly byte[] Magic = [0x51, 0x56, 0x50, 0x42];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) + fields(2) + reserved(4) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Number of fields.</summary>
  public ushort Fields { get; init; }

  /// <summary>Reserved bytes.</summary>
  public uint Reserved { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(QuantelVpbFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static QuantelVpbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bpp = 24,
      Fields = 1,
      PixelData = image.PixelData[..],
    };
  }
}
