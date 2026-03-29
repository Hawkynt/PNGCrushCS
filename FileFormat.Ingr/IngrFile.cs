using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ingr;

/// <summary>Supported INGR data type codes.</summary>
public enum IngrDataType : ushort {
  ByteData = 2,
  Rgb24 = 24,
}

/// <summary>In-memory representation of an Intergraph Raster (INGR) image.</summary>
public sealed class IngrFile : IImageFileFormat<IngrFile> {

  static string IImageFileFormat<IngrFile>.PrimaryExtension => ".cit";
  static string[] IImageFileFormat<IngrFile>.FileExtensions => [".cit", ".itg"];
  static IngrFile IImageFileFormat<IngrFile>.FromFile(FileInfo file) => IngrReader.FromFile(file);
  static IngrFile IImageFileFormat<IngrFile>.FromBytes(byte[] data) => IngrReader.FromBytes(data);
  static IngrFile IImageFileFormat<IngrFile>.FromStream(Stream stream) => IngrReader.FromStream(stream);
  static byte[] IImageFileFormat<IngrFile>.ToBytes(IngrFile file) => IngrWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Data type code indicating pixel format and compression.</summary>
  public IngrDataType DataType { get; init; } = IngrDataType.Rgb24;

  /// <summary>Raw pixel data bytes.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(IngrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.DataType switch {
      IngrDataType.ByteData => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      },
      IngrDataType.Rgb24 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      },
      _ => throw new ArgumentException($"Unsupported INGR data type: {file.DataType}", nameof(file)),
    };
  }

  public static IngrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          DataType = IngrDataType.ByteData,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          DataType = IngrDataType.Rgb24,
          PixelData = image.PixelData[..],
        };
      default:
        throw new ArgumentException($"Unsupported pixel format for INGR: {image.Format}", nameof(image));
    }
  }
}
