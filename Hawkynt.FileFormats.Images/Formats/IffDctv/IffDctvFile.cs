using System;
using FileFormat.Core;

namespace FileFormat.IffDctv;

/// <summary>In-memory representation of an IFF DCTV (Composite Video) image.</summary>
public readonly record struct IffDctvFile : IImageFormatReader<IffDctvFile>, IImageToRawImage<IffDctvFile>, IImageFormatWriter<IffDctvFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for DCTV images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for DCTV images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFormatMetadata<IffDctvFile>.PrimaryExtension => ".dctv";
  static string[] IImageFormatMetadata<IffDctvFile>.FileExtensions => [".dctv"];
  static IffDctvFile IImageFormatReader<IffDctvFile>.FromSpan(ReadOnlySpan<byte> data) => IffDctvReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffDctvFile>.ToBytes(IffDctvFile file) => IffDctvWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this DCTV image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffDctvFile file) {

    var width = file.Width;
    var height = file.Height;
    var rgb = new byte[width * height * 3];

    // Simplified: treat raw data as grayscale luminance hints
    var dataOffset = Math.Min(file.RawData.Length, 100);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var srcIndex = dataOffset + y * width + x;
        var gray = srcIndex < file.RawData.Length ? file.RawData[srcIndex] : (byte)0;
        var offset = (y * width + x) * 3;
        rgb[offset] = gray;
        rgb[offset + 1] = gray;
        rgb[offset + 2] = gray;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
