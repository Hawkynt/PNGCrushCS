using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariGraphics10;

/// <summary>In-memory representation of an Atari Graphics 10 (GTIA 9-color) image. 80x192.</summary>
public sealed class AtariGraphics10File : IImageFileFormat<AtariGraphics10File> {

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 80;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline (40 bytes = 80 pixels / 2 pixels per byte).</summary>
  internal const int BytesPerLine = 40;

  /// <summary>Exact file size in bytes (40 bytes/line x 192 lines).</summary>
  internal const int FileSize = BytesPerLine * PixelHeight;

  /// <summary>Number of colors in the GTIA 9-color mode (4 playfield + 4 player/missile + 1 background).</summary>
  internal const int PaletteColors = 9;

  static string IImageFileFormat<AtariGraphics10File>.PrimaryExtension => ".gr10";
  static string[] IImageFileFormat<AtariGraphics10File>.FileExtensions => [".gr10", ".g10"];
  static FormatCapability IImageFileFormat<AtariGraphics10File>.Capabilities => FormatCapability.IndexedOnly;
  static AtariGraphics10File IImageFileFormat<AtariGraphics10File>.FromFile(FileInfo file) => AtariGraphics10Reader.FromFile(file);
  static AtariGraphics10File IImageFileFormat<AtariGraphics10File>.FromBytes(byte[] data) => AtariGraphics10Reader.FromBytes(data);
  static AtariGraphics10File IImageFileFormat<AtariGraphics10File>.FromStream(Stream stream) => AtariGraphics10Reader.FromStream(stream);
  static RawImage IImageFileFormat<AtariGraphics10File>.ToRawImage(AtariGraphics10File file) => ToRawImage(file);
  static AtariGraphics10File IImageFileFormat<AtariGraphics10File>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AtariGraphics10File>.ToBytes(AtariGraphics10File file) => AtariGraphics10Writer.ToBytes(file);

  /// <summary>Always 80.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw screen data (7680 bytes). Each byte contains 2 pixels in nybbles (upper=left, lower=right), values 0-8.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Default 9-color palette: 4 playfield registers, 4 player/missile registers, 1 background.</summary>
  private static readonly byte[] _DefaultPalette = [
    0x00, 0x00, 0x00, // 0: Background (black)
    0x1C, 0x4B, 0xA0, // 1: PF0 (blue)
    0x6A, 0xA2, 0x32, // 2: PF1 (green)
    0xC8, 0x5C, 0x24, // 3: PF2 (orange)
    0xE8, 0xE8, 0xE8, // 4: PF3 (white)
    0xA8, 0x2A, 0x2A, // 5: PM0 (red)
    0x80, 0x40, 0xC0, // 6: PM1 (purple)
    0x48, 0xA0, 0xA0, // 7: PM2 (cyan)
    0xC0, 0xC0, 0x40, // 8: PM3 (yellow)
  ];

  /// <summary>Converts the Graphics 10 image to an Indexed8 raw image (80x192) with the default 9-color palette.</summary>
  public static RawImage ToRawImage(AtariGraphics10File file) {
    ArgumentNullException.ThrowIfNull(file);

    var indexed = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
      for (var x = 0; x < BytesPerLine; ++x) {
        var srcIndex = y * BytesPerLine + x;
        var b = srcIndex < file.PixelData.Length ? file.PixelData[srcIndex] : (byte)0;

        var leftPixel = (b >> 4) & 0x0F;
        var rightPixel = b & 0x0F;

        // Clamp to valid palette range 0-8
        if (leftPixel > 8) leftPixel = 0;
        if (rightPixel > 8) rightPixel = 0;

        var dstIndex = y * PixelWidth + x * 2;
        indexed[dstIndex] = (byte)leftPixel;
        indexed[dstIndex + 1] = (byte)rightPixel;
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indexed,
      Palette = _DefaultPalette[..],
      PaletteCount = PaletteColors,
    };
  }

  /// <summary>FromRawImage is not supported for Graphics 10 because the palette is hardware-register dependent.</summary>
  public static AtariGraphics10File FromRawImage(RawImage image) =>
    throw new NotSupportedException("Graphics 10 palette is hardware-register dependent; FromRawImage is not supported.");
}
