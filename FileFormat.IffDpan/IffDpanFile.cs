using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffDpan;

/// <summary>In-memory representation of an IFF DPAN (DPaint animation info) file.</summary>
public sealed class IffDpanFile : IImageFileFormat<IffDpanFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for DPAN images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for DPAN images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFileFormat<IffDpanFile>.PrimaryExtension => ".dpan";
  static string[] IImageFileFormat<IffDpanFile>.FileExtensions => [".dpan"];
  static IffDpanFile IImageFileFormat<IffDpanFile>.FromFile(FileInfo file) => IffDpanReader.FromFile(file);
  static IffDpanFile IImageFileFormat<IffDpanFile>.FromBytes(byte[] data) => IffDpanReader.FromBytes(data);
  static IffDpanFile IImageFileFormat<IffDpanFile>.FromStream(Stream stream) => IffDpanReader.FromStream(stream);
  static RawImage IImageFileFormat<IffDpanFile>.ToRawImage(IffDpanFile file) => ToRawImage(file);
  static IffDpanFile IImageFileFormat<IffDpanFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<IffDpanFile>.ToBytes(IffDpanFile file) => IffDpanWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; } = DefaultWidth;

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; } = DefaultHeight;

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts this DPAN file to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffDpanFile file) {
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

  /// <summary>Not supported. DPAN files require DPaint animation encoding.</summary>
  public static IffDpanFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to IffDpanFile is not supported due to DPaint animation encoding requirements.");
  }
}
