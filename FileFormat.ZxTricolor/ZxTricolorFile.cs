using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxTricolor;

/// <summary>In-memory representation of a ZX Spectrum Tricolor file (20736 bytes: three complete 6912-byte screens, interlaced for more colors).</summary>
public sealed class ZxTricolorFile : IImageFileFormat<ZxTricolorFile> {

  static string IImageFileFormat<ZxTricolorFile>.PrimaryExtension => ".3cl";
  static string[] IImageFileFormat<ZxTricolorFile>.FileExtensions => [".3cl"];
  static ZxTricolorFile IImageFileFormat<ZxTricolorFile>.FromFile(FileInfo file) => ZxTricolorReader.FromFile(file);
  static ZxTricolorFile IImageFileFormat<ZxTricolorFile>.FromBytes(byte[] data) => ZxTricolorReader.FromBytes(data);
  static ZxTricolorFile IImageFileFormat<ZxTricolorFile>.FromStream(Stream stream) => ZxTricolorReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxTricolorFile>.ToBytes(ZxTricolorFile file) => ZxTricolorWriter.ToBytes(file);

  /// <summary>ZX Spectrum normal palette (bright=0).</summary>
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

  /// <summary>Screen 1: 6144 bytes bitmap data in linear row order.</summary>
  public byte[] BitmapData1 { get; init; } = [];

  /// <summary>Screen 1: 768 bytes attribute data.</summary>
  public byte[] AttributeData1 { get; init; } = [];

  /// <summary>Screen 2: 6144 bytes bitmap data in linear row order.</summary>
  public byte[] BitmapData2 { get; init; } = [];

  /// <summary>Screen 2: 768 bytes attribute data.</summary>
  public byte[] AttributeData2 { get; init; } = [];

  /// <summary>Screen 3: 6144 bytes bitmap data in linear row order.</summary>
  public byte[] BitmapData3 { get; init; } = [];

  /// <summary>Screen 3: 768 bytes attribute data.</summary>
  public byte[] AttributeData3 { get; init; } = [];

  /// <summary>Converts this tricolor screen to Rgb24 by averaging all three screens pixel by pixel.</summary>
  public static RawImage ToRawImage(ZxTricolorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var cellX = x / 8;
        var cellY = y / 8;
        var attrIndex = cellY * 32 + cellX;

        var color1 = _GetPixelColor(file.BitmapData1, file.AttributeData1, byteIndex, bitPosition, attrIndex);
        var color2 = _GetPixelColor(file.BitmapData2, file.AttributeData2, byteIndex, bitPosition, attrIndex);
        var color3 = _GetPixelColor(file.BitmapData3, file.AttributeData3, byteIndex, bitPosition, attrIndex);

        var r = (((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF) + ((color3 >> 16) & 0xFF)) / 3;
        var g = (((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF) + ((color3 >> 8) & 0xFF)) / 3;
        var b = ((color1 & 0xFF) + (color2 & 0xFF) + (color3 & 0xFF)) / 3;

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)r;
        rgb[offset + 1] = (byte)g;
        rgb[offset + 2] = (byte)b;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  private static int _GetPixelColor(byte[] bitmap, byte[] attributes, int byteIndex, int bitPosition, int attrIndex) {
    var bitValue = (bitmap[byteIndex] >> bitPosition) & 1;
    var attribute = attributes[attrIndex];
    var bright = (attribute >> 6) & 1;
    var paper = (attribute >> 3) & 0x07;
    var ink = attribute & 0x07;
    var palette = bright == 1 ? BrightPalette : NormalPalette;
    return palette[bitValue == 1 ? ink : paper];
  }

  /// <summary>Not supported.</summary>
  public static ZxTricolorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxTricolorFile is not supported due to triple-screen attribute constraints.");
  }
}
