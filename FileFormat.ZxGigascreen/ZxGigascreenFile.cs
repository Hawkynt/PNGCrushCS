using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxGigascreen;

/// <summary>In-memory representation of a ZX Spectrum Gigascreen file (13824 bytes: two complete 6912-byte screens, averaged for more colors).</summary>
public sealed class ZxGigascreenFile : IImageFileFormat<ZxGigascreenFile> {

  static string IImageFileFormat<ZxGigascreenFile>.PrimaryExtension => ".gsc";
  static string[] IImageFileFormat<ZxGigascreenFile>.FileExtensions => [".gsc"];
  static ZxGigascreenFile IImageFileFormat<ZxGigascreenFile>.FromFile(FileInfo file) => ZxGigascreenReader.FromFile(file);
  static ZxGigascreenFile IImageFileFormat<ZxGigascreenFile>.FromBytes(byte[] data) => ZxGigascreenReader.FromBytes(data);
  static ZxGigascreenFile IImageFileFormat<ZxGigascreenFile>.FromStream(Stream stream) => ZxGigascreenReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxGigascreenFile>.ToBytes(ZxGigascreenFile file) => ZxGigascreenWriter.ToBytes(file);

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

  /// <summary>Converts this gigascreen to Rgb24 by averaging two screens pixel by pixel.</summary>
  public static RawImage ToRawImage(ZxGigascreenFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);

        // Screen 1
        var bit1 = (file.BitmapData1[byteIndex] >> bitPosition) & 1;
        var cellX = x / 8;
        var cellY = y / 8;
        var attr1 = file.AttributeData1[cellY * 32 + cellX];
        var bright1 = (attr1 >> 6) & 1;
        var paper1 = (attr1 >> 3) & 0x07;
        var ink1 = attr1 & 0x07;
        var pal1 = bright1 == 1 ? BrightPalette : NormalPalette;
        var color1 = pal1[bit1 == 1 ? ink1 : paper1];

        // Screen 2
        var bit2 = (file.BitmapData2[byteIndex] >> bitPosition) & 1;
        var attr2 = file.AttributeData2[cellY * 32 + cellX];
        var bright2 = (attr2 >> 6) & 1;
        var paper2 = (attr2 >> 3) & 0x07;
        var ink2 = attr2 & 0x07;
        var pal2 = bright2 == 1 ? BrightPalette : NormalPalette;
        var color2 = pal2[bit2 == 1 ? ink2 : paper2];

        // Average RGB values
        var r = (((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2;
        var g = (((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2;
        var b = ((color1 & 0xFF) + (color2 & 0xFF)) / 2;

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

  /// <summary>Not supported.</summary>
  public static ZxGigascreenFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxGigascreenFile is not supported due to dual-screen attribute constraints.");
  }
}
