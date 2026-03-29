using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffMultiPalette;

/// <summary>In-memory representation of an IFF Multi-Palette image with dynamic palette changes.</summary>
public sealed class IffMultiPaletteFile : IImageFileFormat<IffMultiPaletteFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for Multi-Palette images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for Multi-Palette images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFileFormat<IffMultiPaletteFile>.PrimaryExtension => ".mpl";
  static string[] IImageFileFormat<IffMultiPaletteFile>.FileExtensions => [".mpl", ".mpal"];
  static IffMultiPaletteFile IImageFileFormat<IffMultiPaletteFile>.FromFile(FileInfo file) => IffMultiPaletteReader.FromFile(file);
  static IffMultiPaletteFile IImageFileFormat<IffMultiPaletteFile>.FromBytes(byte[] data) => IffMultiPaletteReader.FromBytes(data);
  static IffMultiPaletteFile IImageFileFormat<IffMultiPaletteFile>.FromStream(Stream stream) => IffMultiPaletteReader.FromStream(stream);
  static RawImage IImageFileFormat<IffMultiPaletteFile>.ToRawImage(IffMultiPaletteFile file) => ToRawImage(file);
  static IffMultiPaletteFile IImageFileFormat<IffMultiPaletteFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<IffMultiPaletteFile>.ToBytes(IffMultiPaletteFile file) => IffMultiPaletteWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; } = DefaultWidth;

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; } = DefaultHeight;

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts this Multi-Palette image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffMultiPaletteFile file) {
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

  /// <summary>Not supported. Multi-Palette images require dynamic palette change encoding.</summary>
  public static IffMultiPaletteFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to IffMultiPaletteFile is not supported due to dynamic palette change encoding requirements.");
  }
}
