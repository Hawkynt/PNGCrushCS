using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MetaImage;

/// <summary>In-memory representation of a MetaImage (.mha/.mhd) file used by ITK/VTK.</summary>
public sealed class MetaImageFile : IImageFileFormat<MetaImageFile> {

  static string IImageFileFormat<MetaImageFile>.PrimaryExtension => ".mha";
  static string[] IImageFileFormat<MetaImageFile>.FileExtensions => [".mha", ".mhd"];
  static MetaImageFile IImageFileFormat<MetaImageFile>.FromFile(FileInfo file) => MetaImageReader.FromFile(file);
  static MetaImageFile IImageFileFormat<MetaImageFile>.FromBytes(byte[] data) => MetaImageReader.FromBytes(data);
  static MetaImageFile IImageFileFormat<MetaImageFile>.FromStream(Stream stream) => MetaImageReader.FromStream(stream);
  static byte[] IImageFileFormat<MetaImageFile>.ToBytes(MetaImageFile file) => MetaImageWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public MetaImageElementType ElementType { get; init; }
  public int Channels { get; init; } = 1;
  public bool IsCompressed { get; init; }

  /// <summary>Raw pixel data in the layout specified by <see cref="ElementType"/> and <see cref="Channels"/>.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(MetaImageFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.ElementType != MetaImageElementType.MetUChar)
      throw new NotSupportedException($"ToRawImage only supports {MetaImageElementType.MetUChar}, got {file.ElementType}.");

    var format = file.Channels switch {
      1 => PixelFormat.Gray8,
      3 => PixelFormat.Rgb24,
      _ => throw new NotSupportedException($"Unsupported channel count {file.Channels} for ToRawImage."),
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static MetaImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return image.Format switch {
      PixelFormat.Gray8 => new() {
        Width = image.Width,
        Height = image.Height,
        ElementType = MetaImageElementType.MetUChar,
        Channels = 1,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        ElementType = MetaImageElementType.MetUChar,
        Channels = 3,
        PixelData = image.PixelData[..],
      },
      _ => throw new NotSupportedException($"Unsupported pixel format {image.Format} for FromRawImage."),
    };
  }
}
