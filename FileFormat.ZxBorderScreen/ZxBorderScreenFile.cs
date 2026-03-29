using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxBorderScreen;

/// <summary>In-memory representation of a ZX Spectrum Border Screen file (11136 bytes: 6144 bitmap + 768 attributes + 4224 border data).</summary>
public sealed class ZxBorderScreenFile : IImageFileFormat<ZxBorderScreenFile> {

  static string IImageFileFormat<ZxBorderScreenFile>.PrimaryExtension => ".bsc";
  static string[] IImageFileFormat<ZxBorderScreenFile>.FileExtensions => [".bsc"];
  static ZxBorderScreenFile IImageFileFormat<ZxBorderScreenFile>.FromFile(FileInfo file) => ZxBorderScreenReader.FromFile(file);
  static ZxBorderScreenFile IImageFileFormat<ZxBorderScreenFile>.FromBytes(byte[] data) => ZxBorderScreenReader.FromBytes(data);
  static ZxBorderScreenFile IImageFileFormat<ZxBorderScreenFile>.FromStream(Stream stream) => ZxBorderScreenReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxBorderScreenFile>.ToBytes(ZxBorderScreenFile file) => ZxBorderScreenWriter.ToBytes(file);

  /// <summary>ZX Spectrum normal palette (bright=0): Black, Blue, Red, Magenta, Green, Cyan, Yellow, White.</summary>
  internal static readonly int[] NormalPalette = [
    0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD
  ];

  /// <summary>ZX Spectrum bright palette (bright=1).</summary>
  internal static readonly int[] BrightPalette = [
    0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order (deinterleaved).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>768 bytes of attribute data, one per 8x8 cell (bit 7=flash, bit 6=bright, bits 5-3=paper, bits 2-0=ink).</summary>
  public byte[] AttributeData { get; init; } = [];

  /// <summary>4224 bytes of border color data (one byte per scanline for the full border area).</summary>
  public byte[] BorderData { get; init; } = [];

  /// <summary>Converts this ZX Spectrum Border Screen to a platform-independent <see cref="RawImage"/> in Rgb24 format (256x192 pixel+attribute area only).</summary>
  public static RawImage ToRawImage(ZxBorderScreenFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var cellY = y / 8;
        var attribute = file.AttributeData[cellY * 32 + cellX];
        var bright = (attribute >> 6) & 1;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        var palette = bright == 1 ? BrightPalette : NormalPalette;
        var color = palette[bitValue == 1 ? ink : paper];

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. ZX Spectrum images have complex attribute-based color constraints.</summary>
  public static ZxBorderScreenFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxBorderScreenFile is not supported due to complex attribute-based color constraints.");
  }
}
