using System;
using FileFormat.Core;

namespace FileFormat.XbmColor;

/// <summary>
/// Color X BitMap (XBM-C) file: a C-source text format extending classic XBM with an inline RGB
/// palette and one byte per pixel instead of one bit. Layout:
/// <code>
/// #define name_width  W
/// #define name_height H
/// #define name_colors N
/// static unsigned char name_palette[] = { R0,G0,B0, R1,G1,B1, ... };
/// static unsigned char name_pixels[]  = { i0, i1, ... };
/// </code>
/// </summary>
public readonly record struct XbmColorFile : IImageFormatReader<XbmColorFile>, IImageFormatWriter<XbmColorFile>, IImageToRawImage<XbmColorFile>, IImageFromRawImage<XbmColorFile> {

  static string IImageFormatMetadata<XbmColorFile>.PrimaryExtension => ".xbm";
  static string[] IImageFormatMetadata<XbmColorFile>.FileExtensions => [".xbm"];
  static XbmColorFile IImageFormatReader<XbmColorFile>.FromSpan(ReadOnlySpan<byte> data) => XbmColorReader.FromSpan(data);
  static byte[] IImageFormatWriter<XbmColorFile>.ToBytes(XbmColorFile file) => XbmColorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<XbmColorFile>.VideoModes => [
    new("Color XBM", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])
  ];
  static bool? IImageFormatMetadata<XbmColorFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    // XBM-C identified by the presence of "_colors" near the top of the file.
    if (header.Length < 16) return null;
    var text = System.Text.Encoding.ASCII.GetString(header[..System.Math.Min(header.Length, 1024)]);
    if (text.Contains("_colors") && text.Contains("_palette[]"))
      return true;
    return null;
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public string Name { get; init; }
  public byte[] Palette { get; init; }  // packed RGB triplets, length = ColorCount * 3
  public int ColorCount { get; init; }
  public byte[] PixelData { get; init; } // byte-per-pixel indices into Palette

  public static RawImage ToRawImage(XbmColorFile file) {
    ArgumentNullException.ThrowIfNull(file.Palette);
    ArgumentNullException.ThrowIfNull(file.PixelData);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])file.PixelData.Clone(),
      Palette = (byte[])file.Palette.Clone(),
      PaletteCount = file.ColorCount,
    };
  }

  public static XbmColorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8 || image.Palette is null)
      throw new ArgumentException("Color XBM requires an indexed RawImage with a palette.", nameof(image));
    var count = image.PaletteCount > 0 ? image.PaletteCount : image.Palette.Length / 3;
    return new() {
      Width = image.Width,
      Height = image.Height,
      Name = "image",
      Palette = (byte[])image.Palette.Clone(),
      ColorCount = count,
      PixelData = (byte[])image.PixelData.Clone(),
    };
  }
}
