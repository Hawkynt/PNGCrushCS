using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cals;

/// <summary>In-memory representation of a CALS (MIL-STD-1840) raster image.</summary>
public sealed class CalsFile : IImageFileFormat<CalsFile> {

  static string IImageFileFormat<CalsFile>.PrimaryExtension => ".cal";
  static string[] IImageFileFormat<CalsFile>.FileExtensions => [".cal", ".cals", ".gp4"];
  static CalsFile IImageFileFormat<CalsFile>.FromFile(FileInfo file) => CalsReader.FromFile(file);
  static CalsFile IImageFileFormat<CalsFile>.FromBytes(byte[] data) => CalsReader.FromBytes(data);
  static CalsFile IImageFileFormat<CalsFile>.FromStream(Stream stream) => CalsReader.FromStream(stream);
  static RawImage IImageFileFormat<CalsFile>.ToRawImage(CalsFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<CalsFile>.ToBytes(CalsFile file) => CalsWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Dots per inch (typically 200, 300, or 400).</summary>
  public int Dpi { get; init; } = 200;

  /// <summary>Orientation: "portrait" or "landscape".</summary>
  public string Orientation { get; init; } = "portrait";

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Source document identifier.</summary>
  public string SrcDocId { get; init; } = "NONE";

  /// <summary>Destination document identifier.</summary>
  public string DstDocId { get; init; } = "NONE";

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
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
