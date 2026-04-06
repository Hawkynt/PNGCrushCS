using System;
using FileFormat.Core;

namespace FileFormat.AtariDrg;

/// <summary>In-memory representation of an Atari 8-bit DRG graphics screen dump (160x192, 2bpp, 4 colors).</summary>
public readonly record struct AtariDrgFile : IImageFormatReader<AtariDrgFile>, IImageToRawImage<AtariDrgFile>, IImageFromRawImage<AtariDrgFile>, IImageFormatWriter<AtariDrgFile> {

  /// <summary>Fixed width in pixels.</summary>
  internal const int PixelWidth = 160;

  /// <summary>Fixed height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline row (160 / 4 = 40).</summary>
  internal const int BytesPerRow = 40;

  /// <summary>Bits per pixel.</summary>
  internal const int BitsPerPixel = 2;

  /// <summary>Number of colors.</summary>
  internal const int ColorCount = 4;

  /// <summary>Exact file size in bytes (40 x 192 = 7680).</summary>
  internal const int FileSize = BytesPerRow * PixelHeight;

  static string IImageFormatMetadata<AtariDrgFile>.PrimaryExtension => ".drg";
  static string[] IImageFormatMetadata<AtariDrgFile>.FileExtensions => [".drg"];
  static AtariDrgFile IImageFormatReader<AtariDrgFile>.FromSpan(ReadOnlySpan<byte> data) => AtariDrgReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AtariDrgFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<AtariDrgFile>.ToBytes(AtariDrgFile file) => AtariDrgWriter.ToBytes(file);

  /// <summary>Always 160.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Indexed pixel data (one byte per pixel, values 0-3). Length = 160 x 192 = 30720.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>RGB palette triplets (3 bytes per entry, 4 entries = 12 bytes).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Default grayscale palette for DRG (4 shades).</summary>
  internal static readonly byte[] DefaultPalette = [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255];

  /// <summary>Converts this DRG screen dump to an Indexed8 raw image (160x192, 4-color palette).</summary>
  public static RawImage ToRawImage(AtariDrgFile file) {

    var palette = file.Palette.Length >= ColorCount * 3 ? file.Palette[..(ColorCount * 3)] : DefaultPalette[..];
    var pixelData = new byte[PixelWidth * PixelHeight];
    var srcLen = Math.Min(file.PixelData.Length, pixelData.Length);
    file.PixelData.AsSpan(0, srcLen).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Creates a DRG screen dump from an Indexed8 raw image (160x192, max 4 colors).</summary>
  public static AtariDrgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));
    if (image.PaletteCount > ColorCount)
      throw new ArgumentException($"Expected at most {ColorCount} palette entries but got {image.PaletteCount}.", nameof(image));

    var pixelData = new byte[PixelWidth * PixelHeight];
    var srcLen = Math.Min(image.PixelData.Length, pixelData.Length);
    image.PixelData.AsSpan(0, srcLen).CopyTo(pixelData);

    var palette = image.Palette != null ? image.Palette[..] : DefaultPalette[..];

    return new() {
      PixelData = pixelData,
      Palette = palette,
    };
  }
}
