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

  /// <summary>Orientation: "portrait" or "landscape".</summary>
  public string Orientation { get; init; } = "portrait";

  /// <summary>
  /// Uncompressed 1bpp pixel data, MSB first, ceil(width/8) bytes per row, each bit an index into
  /// the black-then-white palette. On disk this is Group 4 compressed; the reader expands it and the
  /// writer packs it back.
  /// </summary>
  public byte[] PixelData { get; init; }

  /// <summary>Source document identifier.</summary>
  public string SrcDocId { get; init; } = "NONE";

  /// <summary>Destination document identifier.</summary>
  public string DstDocId { get; init; } = "NONE";

  /// <summary>
  /// Index 0 is black and index 1 is white, which is the opposite way round from the fax coding the
  /// pixels arrive in: what Group 4 calls a white run is black ink on a CALS page. Checked both ways
  /// against ImageMagick — a mostly-white image with one small black square compresses to a stream
  /// whose one *black* run is the background.
  /// </summary>
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

    // The palette is fixed by the format, so the indices have to be built against it. A generic
    // quantizer picks its own two entries and may well order them the other way, and the bits it
    // produces then mean the opposite of what a CALS reader will take them for.
    image = image.EnsureIndexed(PixelFormat.Indexed1, _BlackWhitePalette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Dpi = 200,
      PixelData = image.PixelData[..],
    };
  }
}
