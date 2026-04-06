using System;
using FileFormat.Core;

namespace FileFormat.Cals;

/// <summary>In-memory representation of a CALS (MIL-STD-1840) raster image.</summary>
public readonly record struct CalsFile : IImageFormatReader<CalsFile>, IImageToRawImage<CalsFile>, IImageFromRawImage<CalsFile>, IImageFormatWriter<CalsFile> {

  static string IImageFormatMetadata<CalsFile>.PrimaryExtension => ".cal";
  static string[] IImageFormatMetadata<CalsFile>.FileExtensions => [".cal", ".cals", ".gp4"];
  static CalsFile IImageFormatReader<CalsFile>.FromSpan(ReadOnlySpan<byte> data) => CalsReader.FromSpan(data);
  static byte[] IImageFormatWriter<CalsFile>.ToBytes(CalsFile file) => CalsWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Dots per inch (typically 200, 300, or 400).</summary>
  public int Dpi { get; init; }

  /// <summary>Orientation: "portrait" or "landscape".</summary>
  public string Orientation { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Source document identifier.</summary>
  public string SrcDocId { get; init; }

  /// <summary>Destination document identifier.</summary>
  public string DstDocId { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(CalsFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static CalsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Dpi = 200,
      PixelData = image.PixelData[..],
    };
  }
}
