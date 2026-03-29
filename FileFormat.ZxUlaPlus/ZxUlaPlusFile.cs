using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxUlaPlus;

/// <summary>In-memory representation of a ZX Spectrum ULAplus file (6976 bytes: 6144 bitmap + 768 attributes + 64 palette entries).</summary>
public sealed class ZxUlaPlusFile : IImageFileFormat<ZxUlaPlusFile> {

  static string IImageFileFormat<ZxUlaPlusFile>.PrimaryExtension => ".ulp";
  static string[] IImageFileFormat<ZxUlaPlusFile>.FileExtensions => [".ulp"];
  static ZxUlaPlusFile IImageFileFormat<ZxUlaPlusFile>.FromFile(FileInfo file) => ZxUlaPlusReader.FromFile(file);
  static ZxUlaPlusFile IImageFileFormat<ZxUlaPlusFile>.FromBytes(byte[] data) => ZxUlaPlusReader.FromBytes(data);
  static ZxUlaPlusFile IImageFileFormat<ZxUlaPlusFile>.FromStream(Stream stream) => ZxUlaPlusReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxUlaPlusFile>.ToBytes(ZxUlaPlusFile file) => ZxUlaPlusWriter.ToBytes(file);

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order.</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>768 bytes of attribute data, one per 8x8 cell.</summary>
  public byte[] AttributeData { get; init; } = [];

  /// <summary>64 bytes of ULAplus palette entries (3-bit GRB encoding per byte).</summary>
  public byte[] PaletteData { get; init; } = [];

  /// <summary>Decodes a ULAplus palette entry (bits 7-5=G, bits 4-2=R, bits 1-0=B) to an RGB color value.</summary>
  internal static int DecodePaletteEntry(byte entry) {
    var g3 = (entry >> 5) & 0x07;
    var r3 = (entry >> 2) & 0x07;
    var b2 = entry & 0x03;

    // Expand 3-bit to 8-bit: value * 255 / 7
    var r = r3 * 255 / 7;
    var g = g3 * 255 / 7;
    // Expand 2-bit to 8-bit: value * 255 / 3
    var b = b2 * 255 / 3;

    return (r << 16) | (g << 8) | b;
  }

  /// <summary>Converts this ULAplus screen to Rgb24 using the custom 64-color palette.</summary>
  public static RawImage ToRawImage(ZxUlaPlusFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    // ULAplus palette: 64 entries organized as 8 sub-palettes of 8 colors each.
    // Attribute byte selects sub-palette (bits 7-6 select palette group, bits 5-3=paper index, bits 2-0=ink index).
    // Sub-palette index = (attribute >> 6) * 2 for paper, (attribute >> 6) * 2 + 1 for ink (simplified).
    // Actually: palette index = (attribute & 0xC0) >> 2 | (color & 0x07) for each of ink/paper.

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var cellY = y / 8;
        var attribute = file.AttributeData[cellY * 32 + cellX];

        var paletteGroup = (attribute >> 6) & 0x03;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        // ULAplus palette layout: group * 16 + color for ink (0-7), group * 16 + 8 + color for paper
        int paletteIndex;
        if (bitValue == 1)
          paletteIndex = paletteGroup * 16 + ink;
        else
          paletteIndex = paletteGroup * 16 + 8 + paper;

        // Clamp to 64 entries
        if (paletteIndex >= 64)
          paletteIndex = 0;

        var color = DecodePaletteEntry(file.PaletteData[paletteIndex]);

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

  /// <summary>Not supported.</summary>
  public static ZxUlaPlusFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxUlaPlusFile is not supported due to ULAplus palette constraints.");
  }
}
