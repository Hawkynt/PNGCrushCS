using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxTimex;

/// <summary>In-memory representation of a Timex HiColor file (12288 bytes: 6144 bitmap + 6144 per-scanline-row extended attributes).</summary>
public sealed class ZxTimexFile : IImageFileFormat<ZxTimexFile> {

  static string IImageFileFormat<ZxTimexFile>.PrimaryExtension => ".tmx";
  static string[] IImageFileFormat<ZxTimexFile>.FileExtensions => [".tmx"];
  static ZxTimexFile IImageFileFormat<ZxTimexFile>.FromFile(FileInfo file) => ZxTimexReader.FromFile(file);
  static ZxTimexFile IImageFileFormat<ZxTimexFile>.FromBytes(byte[] data) => ZxTimexReader.FromBytes(data);
  static ZxTimexFile IImageFileFormat<ZxTimexFile>.FromStream(Stream stream) => ZxTimexReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxTimexFile>.ToBytes(ZxTimexFile file) => ZxTimexWriter.ToBytes(file);

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

  /// <summary>6144 bytes of per-scanline-row extended attribute data (32 per row, 192 rows).</summary>
  public byte[] AttributeData { get; init; } = [];

  /// <summary>Converts this Timex HiColor screen to Rgb24.</summary>
  public static RawImage ToRawImage(ZxTimexFile file) {
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
        var attribute = file.AttributeData[y * 32 + cellX];
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
  public static ZxTimexFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxTimexFile is not supported due to complex per-scanline attribute constraints.");
  }
}
