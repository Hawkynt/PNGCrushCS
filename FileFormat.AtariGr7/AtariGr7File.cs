using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariGr7;

/// <summary>In-memory representation of an Atari 8-bit Graphics Mode 7 screen dump (160x96, 2bpp, 4 colors).</summary>
public sealed class AtariGr7File : IImageFileFormat<AtariGr7File> {

  /// <summary>Fixed width in pixels.</summary>
  internal const int PixelWidth = 160;

  /// <summary>Fixed height in pixels.</summary>
  internal const int PixelHeight = 96;

  /// <summary>Bytes per scanline row (160 / 4 = 40).</summary>
  internal const int BytesPerRow = 40;

  /// <summary>Bits per pixel.</summary>
  internal const int BitsPerPixel = 2;

  /// <summary>Number of colors.</summary>
  internal const int ColorCount = 4;

  /// <summary>Exact file size in bytes (40 x 96 = 3840).</summary>
  internal const int FileSize = BytesPerRow * PixelHeight;

  static string IImageFileFormat<AtariGr7File>.PrimaryExtension => ".gr7";
  static string[] IImageFileFormat<AtariGr7File>.FileExtensions => [".gr7"];
  static FormatCapability IImageFileFormat<AtariGr7File>.Capabilities => FormatCapability.IndexedOnly;
  static AtariGr7File IImageFileFormat<AtariGr7File>.FromFile(FileInfo file) => AtariGr7Reader.FromFile(file);
  static AtariGr7File IImageFileFormat<AtariGr7File>.FromBytes(byte[] data) => AtariGr7Reader.FromBytes(data);
  static AtariGr7File IImageFileFormat<AtariGr7File>.FromStream(Stream stream) => AtariGr7Reader.FromStream(stream);
  static RawImage IImageFileFormat<AtariGr7File>.ToRawImage(AtariGr7File file) => ToRawImage(file);
  static AtariGr7File IImageFileFormat<AtariGr7File>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AtariGr7File>.ToBytes(AtariGr7File file) => AtariGr7Writer.ToBytes(file);

  /// <summary>Always 160.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 96.</summary>
  public int Height => PixelHeight;

  /// <summary>Indexed pixel data (one byte per pixel, values 0-3). Length = 160 x 96 = 15360.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>RGB palette triplets (3 bytes per entry, 4 entries = 12 bytes).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Default grayscale palette for GR.7 (PF0-PF3).</summary>
  internal static readonly byte[] DefaultPalette = [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255];

  /// <summary>Converts this GR.7 screen dump to an Indexed8 raw image (160x96, 4-color palette).</summary>
  public static RawImage ToRawImage(AtariGr7File file) {
    ArgumentNullException.ThrowIfNull(file);

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

  /// <summary>Creates a GR.7 screen dump from an Indexed8 raw image (160x96, max 4 colors).</summary>
  public static AtariGr7File FromRawImage(RawImage image) {
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
