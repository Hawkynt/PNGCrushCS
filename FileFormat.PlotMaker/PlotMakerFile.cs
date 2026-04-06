using System;
using FileFormat.Core;

namespace FileFormat.PlotMaker;

/// <summary>In-memory representation of a Plot Maker monochrome image.</summary>
public readonly record struct PlotMakerFile : IImageFormatReader<PlotMakerFile>, IImageToRawImage<PlotMakerFile>, IImageFromRawImage<PlotMakerFile>, IImageFormatWriter<PlotMakerFile> {

  static string IImageFormatMetadata<PlotMakerFile>.PrimaryExtension => ".plt";
  static string[] IImageFormatMetadata<PlotMakerFile>.FileExtensions => [".plt", ".plm2"];
  static PlotMakerFile IImageFormatReader<PlotMakerFile>.FromSpan(ReadOnlySpan<byte> data) => PlotMakerReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PlotMakerFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<PlotMakerFile>.ToBytes(PlotMakerFile file) => PlotMakerWriter.ToBytes(file);

  /// <summary>Size of the header in bytes (2 width + 2 height).</summary>
  internal const int HeaderSize = 4;

  /// <summary>Image width in pixels.</summary>
  public ushort Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public ushort Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this Plot Maker image to a platform-independent <see cref="RawImage"/> in Indexed1 format.</summary>
  public static RawImage ToRawImage(PlotMakerFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Plot Maker image from a platform-independent <see cref="RawImage"/>. Requires Indexed1 format.</summary>
  public static PlotMakerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));

    return new() {
      Width = (ushort)image.Width,
      Height = (ushort)image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
