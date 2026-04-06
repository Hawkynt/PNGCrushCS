using System;
using FileFormat.Core;

namespace FileFormat.IffHame;

/// <summary>In-memory representation of an IFF HAM-E (HAM Enhanced, 18-bit color) image.</summary>
public readonly record struct IffHameFile : IImageFormatReader<IffHameFile>, IImageToRawImage<IffHameFile>, IImageFormatWriter<IffHameFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for HAM-E images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for HAM-E images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFormatMetadata<IffHameFile>.PrimaryExtension => ".hame";
  static string[] IImageFormatMetadata<IffHameFile>.FileExtensions => [".hame"];
  static IffHameFile IImageFormatReader<IffHameFile>.FromSpan(ReadOnlySpan<byte> data) => IffHameReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffHameFile>.ToBytes(IffHameFile file) => IffHameWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this HAM-E image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffHameFile file) {

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
