using System;
using FileFormat.Core;

namespace FileFormat.IffDpan;

/// <summary>In-memory representation of an IFF DPAN (DPaint animation info) file.</summary>
public readonly record struct IffDpanFile : IImageFormatReader<IffDpanFile>, IImageToRawImage<IffDpanFile>, IImageFormatWriter<IffDpanFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for DPAN images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for DPAN images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFormatMetadata<IffDpanFile>.PrimaryExtension => ".dpan";
  static string[] IImageFormatMetadata<IffDpanFile>.FileExtensions => [".dpan"];
  static IffDpanFile IImageFormatReader<IffDpanFile>.FromSpan(ReadOnlySpan<byte> data) => IffDpanReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffDpanFile>.ToBytes(IffDpanFile file) => IffDpanWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this DPAN file to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffDpanFile file) {

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

}
