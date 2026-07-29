using System;
using FileFormat.Core;

namespace FileFormat.PaintShop;

/// <summary>In-memory representation of a PaintShop picture (.da4) for the Atari ST.</summary>
/// <remarks>
/// A bare 640x800 bitmap at one bit per pixel — no header, no palette. The page is twice as tall as
/// the ST's monochrome screen because PaintShop drew for a printer rather than a monitor, so the
/// 64000 bytes are exactly one sheet. A set bit is ink on white paper.
/// </remarks>
public readonly record struct PaintShopFile
  : IImageFormatReader<PaintShopFile>, IImageToRawImage<PaintShopFile>,
    IImageFromRawImage<PaintShopFile>, IImageFormatWriter<PaintShopFile> {

  /// <summary>Page width.</summary>
  public const int Width = 640;

  /// <summary>Page height.</summary>
  public const int Height = 800;

  /// <summary>Bytes per row.</summary>
  public const int BytesPerRow = Width / 8;

  /// <summary>Total file size, which is the bitmap and nothing else.</summary>
  public const int FileSize = BytesPerRow * Height;

  static string IImageFormatMetadata<PaintShopFile>.PrimaryExtension => ".da4";
  static string[] IImageFormatMetadata<PaintShopFile>.FileExtensions => [".da4"];
  static PaintShopFile IImageFormatReader<PaintShopFile>.FromSpan(ReadOnlySpan<byte> data) => PaintShopReader.FromSpan(data);
  static byte[] IImageFormatWriter<PaintShopFile>.ToBytes(PaintShopFile file) => PaintShopWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PaintShopFile>.VideoModes => [
    new("PaintShop page", [(Width, Height)], [2])
  ];

  /// <summary>The bitmap, one bit per pixel, most significant bit leftmost.</summary>
  public byte[] BitmapData { get; init; }

  public static RawImage ToRawImage(PaintShopFile file)
    => MonochromePage.Decode(file.BitmapData ?? [], Width, Height, inkIsWhite: false);

  public static PaintShopFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { BitmapData = MonochromePage.Encode(image, Width, Height, inkIsWhite: false) };
  }
}
