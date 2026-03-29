using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PlotMaker;

/// <summary>In-memory representation of a Plot Maker monochrome image.</summary>
public sealed class PlotMakerFile : IImageFileFormat<PlotMakerFile> {

  static string IImageFileFormat<PlotMakerFile>.PrimaryExtension => ".plt";
  static string[] IImageFileFormat<PlotMakerFile>.FileExtensions => [".plt", ".plm2"];
  static FormatCapability IImageFileFormat<PlotMakerFile>.Capabilities => FormatCapability.MonochromeOnly;
  static PlotMakerFile IImageFileFormat<PlotMakerFile>.FromFile(FileInfo file) => PlotMakerReader.FromFile(file);
  static PlotMakerFile IImageFileFormat<PlotMakerFile>.FromBytes(byte[] data) => PlotMakerReader.FromBytes(data);
  static PlotMakerFile IImageFileFormat<PlotMakerFile>.FromStream(Stream stream) => PlotMakerReader.FromStream(stream);
  static byte[] IImageFileFormat<PlotMakerFile>.ToBytes(PlotMakerFile file) => PlotMakerWriter.ToBytes(file);

  /// <summary>Size of the header in bytes (2 width + 2 height).</summary>
  internal const int HeaderSize = 4;

  /// <summary>Image width in pixels.</summary>
  public ushort Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public ushort Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this Plot Maker image to a platform-independent <see cref="RawImage"/> in Indexed1 format.</summary>
  public static RawImage ToRawImage(PlotMakerFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
