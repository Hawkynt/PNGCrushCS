using System;
using FileFormat.Core;

namespace FileFormat.ZxNextImage;

/// <summary>In-memory representation of a ZX Spectrum Next (.nxi) image.</summary>
/// <remarks>
/// A fixed 49664-byte file: a 512-byte palette of 256 nine-bit colours followed by 256x192 bytes,
/// one palette index per pixel. Each palette entry is two bytes — the first packs red, green and
/// the top two bits of blue as 3-3-2, and the low bit of the second byte supplies blue's third
/// bit, giving three bits per channel.
/// </remarks>
public readonly record struct ZxNextImageFile
  : IImageFormatReader<ZxNextImageFile>, IImageToRawImage<ZxNextImageFile>,
    IImageFromRawImage<ZxNextImageFile>, IImageFormatWriter<ZxNextImageFile> {

  /// <summary>Image width.</summary>
  public const int ImageWidth = 256;

  /// <summary>Image height.</summary>
  public const int ImageHeight = 192;

  /// <summary>Palette entries.</summary>
  public const int ColorCount = 256;

  /// <summary>Bytes per palette entry.</summary>
  public const int BytesPerColor = 2;

  /// <summary>Size of the palette block.</summary>
  public const int PaletteDataSize = ColorCount * BytesPerColor;

  /// <summary>Offset of the pixel data.</summary>
  public const int PixelDataOffset = PaletteDataSize;

  /// <summary>Size of the pixel data.</summary>
  public const int PixelDataSize = ImageWidth * ImageHeight;

  /// <summary>Total file size.</summary>
  public const int FileSize = PixelDataOffset + PixelDataSize;

  static string IImageFormatMetadata<ZxNextImageFile>.PrimaryExtension => ".nxi";
  static string[] IImageFormatMetadata<ZxNextImageFile>.FileExtensions => [".nxi"];
  static ZxNextImageFile IImageFormatReader<ZxNextImageFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxNextImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxNextImageFile>.ToBytes(ZxNextImageFile file)
    => ZxNextImageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxNextImageFile>.VideoModes => [
    new("Layer 2", [(ImageWidth, ImageHeight)], [ColorCount])
  ];

  /// <summary>Raw palette block, two bytes per entry.</summary>
  public byte[] PaletteData { get; init; }

  /// <summary>One palette index per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Expands a three-bit channel to eight bits, spreading 0..7 across the full range.</summary>
  private static byte _Expand3(int value) => (byte)(value * 73 >> 1);

  /// <summary>Reduces an eight-bit channel to three bits.</summary>
  private static int _Reduce3(byte value) => (value * 7 + 127) / 255;

  public static RawImage ToRawImage(ZxNextImageFile file) {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var high = file.PaletteData[i * BytesPerColor];
      var low = file.PaletteData[i * BytesPerColor + 1];
      palette[i * 3] = _Expand3(high >> 5);
      palette[i * 3 + 1] = _Expand3((high >> 2) & 7);
      palette[i * 3 + 2] = _Expand3(((high & 3) << 1) | (low & 1));
    }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static ZxNextImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != ImageWidth || image.Height != ImageHeight)
      throw new ArgumentException($"Expected {ImageWidth}x{ImageHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed8);
    var rgb = indexed.Palette ?? [];

    var paletteData = new byte[PaletteDataSize];
    for (var i = 0; i < ColorCount && i * 3 + 2 < rgb.Length; ++i) {
      var r = _Reduce3(rgb[i * 3]);
      var g = _Reduce3(rgb[i * 3 + 1]);
      var b = _Reduce3(rgb[i * 3 + 2]);
      paletteData[i * BytesPerColor] = (byte)((r << 5) | (g << 2) | (b >> 1));
      paletteData[i * BytesPerColor + 1] = (byte)(b & 1);
    }

    var pixels = new byte[PixelDataSize];
    indexed.PixelData.AsSpan(0, Math.Min(indexed.PixelData.Length, PixelDataSize)).CopyTo(pixels);

    return new() { PaletteData = paletteData, PixelData = pixels };
  }
}
