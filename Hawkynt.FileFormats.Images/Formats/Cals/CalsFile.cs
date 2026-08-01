using System;
using FileFormat.Core;

namespace FileFormat.Cals;

/// <summary>In-memory representation of a CALS (MIL-STD-1840) raster image.</summary>
public readonly record struct CalsFile() : IImageFormatReader<CalsFile>, IImageToRawImage<CalsFile>, IImageFromRawImage<CalsFile>, IImageFormatWriter<CalsFile> {

  static string IImageFormatMetadata<CalsFile>.PrimaryExtension => ".cal";
  static string[] IImageFormatMetadata<CalsFile>.FileExtensions => [".cal", ".cals", ".gp4"];
  static CalsFile IImageFormatReader<CalsFile>.FromSpan(ReadOnlySpan<byte> data) => CalsReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CalsFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];
  static byte[] IImageFormatWriter<CalsFile>.ToBytes(CalsFile file) => CalsWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Dots per inch (typically 200, 300, or 400).</summary>
  public int Dpi { get; init; } = 200;

  /// <summary>The angles the rows and the columns run at, as the <c>rorient</c> record states them.</summary>
  public string Orientation { get; init; } = DefaultOrientation;

  /// <summary>Rows left to right, columns top to bottom, which is how a picture is normally stored.</summary>
  public const string DefaultOrientation = "000,270";

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Source document identifier.</summary>
  public string SrcDocId { get; init; } = "NONE";

  /// <summary>Destination document identifier.</summary>
  public string DstDocId { get; init; } = "NONE";

  /// <summary>A set bit is black, which is what the fax coding underneath this counts in.</summary>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(CalsFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData, file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static CalsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Dpi = 200,
      PixelData = BilevelRows.Pack(BilevelRows.Threshold(image, setWhenDark: true), image.Width, image.Height),
    };
  }
}
