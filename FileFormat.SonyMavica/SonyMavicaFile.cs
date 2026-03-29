using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SonyMavica;

/// <summary>In-memory representation of a Sony Mavica .411 image.</summary>
public sealed class SonyMavicaFile : IImageFileFormat<SonyMavicaFile> {

  static string IImageFileFormat<SonyMavicaFile>.PrimaryExtension => ".411";
  static string[] IImageFileFormat<SonyMavicaFile>.FileExtensions => [".411"];
  static SonyMavicaFile IImageFileFormat<SonyMavicaFile>.FromFile(FileInfo file) => SonyMavicaReader.FromFile(file);
  static SonyMavicaFile IImageFileFormat<SonyMavicaFile>.FromBytes(byte[] data) => SonyMavicaReader.FromBytes(data);
  static SonyMavicaFile IImageFileFormat<SonyMavicaFile>.FromStream(Stream stream) => SonyMavicaReader.FromStream(stream);
  static byte[] IImageFileFormat<SonyMavicaFile>.ToBytes(SonyMavicaFile file) => SonyMavicaWriter.ToBytes(file);

  /// <summary>Magic bytes: "MV" (0x4D 0x56).</summary>
  internal static readonly byte[] Magic = [0x4D, 0x56];

  /// <summary>Header size in bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Image format code.</summary>
  public ushort Format { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this Mavica image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SonyMavicaFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Not supported.</summary>
  public static SonyMavicaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to SonyMavicaFile is not supported.");
  }
}
