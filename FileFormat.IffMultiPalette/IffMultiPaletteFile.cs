using System;
using FileFormat.Core;

namespace FileFormat.IffMultiPalette;

/// <summary>In-memory representation of an IFF Multi-Palette image with dynamic palette changes.</summary>
public readonly record struct IffMultiPaletteFile : IImageFormatReader<IffMultiPaletteFile>, IImageToRawImage<IffMultiPaletteFile>, IImageFormatWriter<IffMultiPaletteFile> {

  /// <summary>Minimum valid file size (FORM header = 12 bytes).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Default width for Multi-Palette images.</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height for Multi-Palette images.</summary>
  internal const int DefaultHeight = 200;

  static string IImageFormatMetadata<IffMultiPaletteFile>.PrimaryExtension => ".mpl";
  static string[] IImageFormatMetadata<IffMultiPaletteFile>.FileExtensions => [".mpl", ".mpal"];
  static IffMultiPaletteFile IImageFormatReader<IffMultiPaletteFile>.FromSpan(ReadOnlySpan<byte> data) => IffMultiPaletteReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffMultiPaletteFile>.ToBytes(IffMultiPaletteFile file) => IffMultiPaletteWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Multi-Palette image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(IffMultiPaletteFile file) {

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
