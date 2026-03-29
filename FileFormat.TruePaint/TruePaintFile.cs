using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.TruePaint;

/// <summary>In-memory representation of a True Paint interlace multicolor image (.mci).</summary>
public sealed class TruePaintFile : IImageFileFormat<TruePaintFile> {

  static string IImageFileFormat<TruePaintFile>.PrimaryExtension => ".mci";
  static string[] IImageFileFormat<TruePaintFile>.FileExtensions => [".mci"];
  static TruePaintFile IImageFileFormat<TruePaintFile>.FromFile(FileInfo file) => TruePaintReader.FromFile(file);
  static TruePaintFile IImageFileFormat<TruePaintFile>.FromBytes(byte[] data) => TruePaintReader.FromBytes(data);
  static TruePaintFile IImageFileFormat<TruePaintFile>.FromStream(Stream stream) => TruePaintReader.FromStream(stream);
  static byte[] IImageFileFormat<TruePaintFile>.ToBytes(TruePaintFile file) => TruePaintWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of a bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of a screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the background/border section in bytes.</summary>
  internal const int BackgroundBorderSize = 2;

  /// <summary>Size of the trailing padding in bytes.</summary>
  internal const int PaddingSize = 430;

  /// <summary>Total uncompressed payload size (8000 + 1000 + 8000 + 1000 + 1000 + 2 + 430).</summary>
  internal const int UncompressedPayloadSize = BitmapDataSize + ScreenRamSize + BitmapDataSize + ScreenRamSize + ColorRamSize + BackgroundBorderSize + PaddingSize;

  /// <summary>Expected total file size including the 2-byte load address.</summary>
  public const int ExpectedFileSize = LoadAddressSize + UncompressedPayloadSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian, typically $9C00).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>First multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData1 { get; init; } = [];

  /// <summary>First screen RAM (1000 bytes).</summary>
  public byte[] ScreenRam1 { get; init; } = [];

  /// <summary>Second multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData2 { get; init; } = [];

  /// <summary>Second screen RAM (1000 bytes).</summary>
  public byte[] ScreenRam2 { get; init; } = [];

  /// <summary>Color RAM shared by both bitmaps (1000 bytes).</summary>
  public byte[] ColorRam { get; init; } = [];

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Converts this True Paint image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(TruePaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var pixelInByte = x % 4;
        var shift = (3 - pixelInByte) * 2;

        var bitmapByte1 = file.BitmapData1[cellIndex * 8 + byteInCell];
        var bitValue1 = (bitmapByte1 >> shift) & 0x03;
        var colorIndex1 = bitValue1 switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenRam1[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenRam1[cellIndex] & 0x0F,
          3 => file.ColorRam[cellIndex] & 0x0F,
          _ => 0
        };

        var bitmapByte2 = file.BitmapData2[cellIndex * 8 + byteInCell];
        var bitValue2 = (bitmapByte2 >> shift) & 0x03;
        var colorIndex2 = bitValue2 switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenRam2[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenRam2[cellIndex] & 0x0F,
          3 => file.ColorRam[cellIndex] & 0x0F,
          _ => 0
        };

        var color1 = _C64Palette[colorIndex1];
        var color2 = _C64Palette[colorIndex2];

        var r = (byte)((((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2);
        var g = (byte)((((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2);
        var b = (byte)(((color1 & 0xFF) + (color2 & 0xFF)) / 2);

        var offset = (y * width + x) * 3;
        rgb[offset] = r;
        rgb[offset + 1] = g;
        rgb[offset + 2] = b;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. True Paint images have complex cell-based interlace color constraints.</summary>
  public static TruePaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to TruePaintFile is not supported due to complex cell-based interlace color constraints.");
  }
}
