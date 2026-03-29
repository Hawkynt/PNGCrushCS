using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffHame;

/// <summary>In-memory representation of an IFF HAM-E (HAM Enhanced, 18-bit color) image.</summary>
public sealed class IffHameFile : IImageFileFormat<IffHameFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for HAM-E images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for HAM-E images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFileFormat<IffHameFile>.PrimaryExtension => ".hame";
  static string[] IImageFileFormat<IffHameFile>.FileExtensions => [".hame"];
  static IffHameFile IImageFileFormat<IffHameFile>.FromFile(FileInfo file) => IffHameReader.FromFile(file);
  static IffHameFile IImageFileFormat<IffHameFile>.FromBytes(byte[] data) => IffHameReader.FromBytes(data);
  static IffHameFile IImageFileFormat<IffHameFile>.FromStream(Stream stream) => IffHameReader.FromStream(stream);
  static RawImage IImageFileFormat<IffHameFile>.ToRawImage(IffHameFile file) => ToRawImage(file);
  static IffHameFile IImageFileFormat<IffHameFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<IffHameFile>.ToBytes(IffHameFile file) => IffHameWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; } = DefaultWidth;

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; } = DefaultHeight;

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts this HAM-E image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffHameFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var rgb = new byte[width * height * 3];

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. HAM-E images require complex 18-bit color encoding.</summary>
  public static IffHameFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to IffHameFile is not supported due to complex 18-bit HAM-E encoding.");
  }
}
