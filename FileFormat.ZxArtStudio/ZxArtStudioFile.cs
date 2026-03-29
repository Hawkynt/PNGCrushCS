using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxArtStudio;

/// <summary>In-memory representation of a ZX Spectrum Art Studio file (6912 bytes: 6144 bitmap + 768 attributes).</summary>
public sealed class ZxArtStudioFile : IImageFileFormat<ZxArtStudioFile> {

  static string IImageFileFormat<ZxArtStudioFile>.PrimaryExtension => ".zas";
  static string[] IImageFileFormat<ZxArtStudioFile>.FileExtensions => [".zas"];
  static ZxArtStudioFile IImageFileFormat<ZxArtStudioFile>.FromFile(FileInfo file) => ZxArtStudioReader.FromFile(file);
  static ZxArtStudioFile IImageFileFormat<ZxArtStudioFile>.FromBytes(byte[] data) => ZxArtStudioReader.FromBytes(data);
  static ZxArtStudioFile IImageFileFormat<ZxArtStudioFile>.FromStream(Stream stream) => ZxArtStudioReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxArtStudioFile>.ToBytes(ZxArtStudioFile file) => ZxArtStudioWriter.ToBytes(file);

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

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order.</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>768 bytes of attribute data, one per 8x8 cell.</summary>
  public byte[] AttributeData { get; init; } = [];

  /// <summary>Converts this Art Studio screen to Rgb24.</summary>
  public static RawImage ToRawImage(ZxArtStudioFile file) {
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

  /// <summary>Not supported.</summary>
  public static ZxArtStudioFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxArtStudioFile is not supported due to complex attribute-based color constraints.");
  }
}
