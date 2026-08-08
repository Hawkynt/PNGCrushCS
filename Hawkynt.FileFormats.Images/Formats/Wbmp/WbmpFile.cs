using System;
using FileFormat.Core;

namespace FileFormat.Wbmp;

/// <summary>In-memory representation of a WBMP (Wireless Bitmap) image.</summary>
[FormatMimeType("image/vnd.wap.wbmp")]
public readonly record struct WbmpFile : IImageFormatReader<WbmpFile>, IImageToRawImage<WbmpFile>, IImageFromRawImage<WbmpFile>, IImageFormatWriter<WbmpFile> {

  static string IImageFormatMetadata<WbmpFile>.PrimaryExtension => ".wbmp";
  /// <summary>Also the two abbreviations the wireless bitmap is saved under.</summary>
  static string[] IImageFormatMetadata<WbmpFile>.FileExtensions => [".wbmp", ".wbm", ".wap"];
  static WbmpFile IImageFormatReader<WbmpFile>.FromSpan(ReadOnlySpan<byte> data) => WbmpReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<WbmpFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<WbmpFile>.ToBytes(WbmpFile file) => WbmpWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(WbmpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData, file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static WbmpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = BilevelRows.Pack(
        BilevelRows.Threshold(image, setWhenDark: false), image.Width, image.Height),
    };
  }
}
