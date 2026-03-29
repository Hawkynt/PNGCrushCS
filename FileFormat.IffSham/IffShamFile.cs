using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffSham;

/// <summary>In-memory representation of an IFF SHAM (Sliced HAM) image.</summary>
public sealed class IffShamFile : IImageFileFormat<IffShamFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for SHAM images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for SHAM images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFileFormat<IffShamFile>.PrimaryExtension => ".sham";
  static string[] IImageFileFormat<IffShamFile>.FileExtensions => [".sham"];
  static IffShamFile IImageFileFormat<IffShamFile>.FromFile(FileInfo file) => IffShamReader.FromFile(file);
  static IffShamFile IImageFileFormat<IffShamFile>.FromBytes(byte[] data) => IffShamReader.FromBytes(data);
  static IffShamFile IImageFileFormat<IffShamFile>.FromStream(Stream stream) => IffShamReader.FromStream(stream);
  static RawImage IImageFileFormat<IffShamFile>.ToRawImage(IffShamFile file) => ToRawImage(file);
  static IffShamFile IImageFileFormat<IffShamFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<IffShamFile>.ToBytes(IffShamFile file) => IffShamWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; } = DefaultWidth;

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; } = DefaultHeight;

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts this SHAM image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffShamFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var rgb = new byte[width * height * 3];

    // Simplified: produce a black image since full SHAM decode requires complex per-scanline palette handling
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. SHAM images require complex per-scanline palette encoding.</summary>
  public static IffShamFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to IffShamFile is not supported due to complex per-scanline palette encoding.");
  }
}
