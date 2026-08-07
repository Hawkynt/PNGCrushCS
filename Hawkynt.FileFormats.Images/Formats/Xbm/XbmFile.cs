using System;
using FileFormat.Core;

namespace FileFormat.Xbm;

/// <summary>In-memory representation of an XBM (X BitMap) image.</summary>
[FormatMimeType("image/x-xbitmap", "image/x-xbm")]
public readonly record struct XbmFile : IImageFormatReader<XbmFile>, IImageToRawImage<XbmFile>, IImageFromRawImage<XbmFile>, IImageFormatWriter<XbmFile> {

  static string IImageFormatMetadata<XbmFile>.PrimaryExtension => ".xbm";
  /// <summary>
  /// Also <c>.icon</c>, which is what X11 calls the same thing.
  /// </summary>
  /// <remarks>
  /// Only the Sun icon claimed that name, and a Sun icon opens with a C comment where this opens
  /// with a #define — so a real X11 icon was refused for not being the other format.
  /// </remarks>
  static string[] IImageFormatMetadata<XbmFile>.FileExtensions => [".xbm", ".icon"];
  static XbmFile IImageFormatReader<XbmFile>.FromSpan(ReadOnlySpan<byte> data) => XbmReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<XbmFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<XbmFile>.ToBytes(XbmFile file) => XbmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public string Name { get; init; }
  public int? HotspotX { get; init; }
  public int? HotspotY { get; init; }

  /// <summary>1bpp packed pixel data, LSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Index zero is paper, index one is ink.</summary>
  /// <remarks>
  /// A set bit means a dot was drawn, not that the pixel is lit. Reading it the other way gives a
  /// negative of the picture, which round-trips through our own writer perfectly well.
  /// </remarks>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  private static byte _ReverseBits(byte b) {
    var result = 0;
    for (var i = 0; i < 8; ++i) {
      result = (result << 1) | (b & 1);
      b >>= 1;
    }
    return (byte)result;
  }

  public static RawImage ToRawImage(XbmFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData, file.Width, file.Height, mostSignificantFirst: false),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static XbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Name = "image",
      PixelData = BilevelRows.Pack(
        BilevelRows.Threshold(image, setWhenDark: true), image.Width, image.Height, mostSignificantFirst: false),
    };
  }
}
