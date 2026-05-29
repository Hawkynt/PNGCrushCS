using System;
using FileFormat.Core;

namespace FileFormat.GunPaint;

/// <summary>In-memory representation of a C64 GunPaint FLI (Flexible Line Interpretation) multicolor image.</summary>
public readonly record struct GunPaintFile : IImageFormatReader<GunPaintFile>, IImageToRawImage<GunPaintFile>, IImageFormatWriter<GunPaintFile> {

  static string IImageFormatMetadata<GunPaintFile>.PrimaryExtension => ".gun";
  static string[] IImageFormatMetadata<GunPaintFile>.FileExtensions => [".gun"];
  static GunPaintFile IImageFormatReader<GunPaintFile>.FromSpan(ReadOnlySpan<byte> data) => GunPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<GunPaintFile>.ToBytes(GunPaintFile file) => GunPaintWriter.ToBytes(file);

  /// <summary>The fixed width of a GunPaint image in multicolor pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a GunPaint image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes.</summary>
  public const int ExpectedFileSize = 33603;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Offset of bitmap data within the raw data (after load address).</summary>
  internal const int BitmapDataOffset = 0;

  /// <summary>Offset of screen RAM within the raw data (after load address).</summary>
  internal const int ScreenRamOffset = BitmapDataSize;

  /// <summary>Size of the raw data payload (file size minus load address).</summary>
  internal const int RawDataSize = ExpectedFileSize - LoadAddressSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width in multicolor pixels, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height in pixels, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian), typically 0x4000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw file payload after the 2-byte load address (33601 bytes). Contains bitmap data, screen RAM, color RAM, FLI color banks, and sprite data at fixed offsets.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this GunPaint image to a platform-independent <see cref="RawImage"/> in Rgb24 format using simplified multicolor decoding (without full FLI processing).</summary>
  public static RawImage ToRawImage(GunPaintFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];
    var raw = file.RawData;

    if (raw.Length >= BitmapDataSize + ScreenRamSize) {
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var cellX = x / 4;
          var cellY = y / 8;
          var cellIndex = cellY * 40 + cellX;
          var byteInCell = y % 8;
          var bitmapIdx = BitmapDataOffset + cellIndex * 8 + byteInCell;
          var bitmapByte = bitmapIdx < raw.Length ? raw[bitmapIdx] : (byte)0;
          var pixelInByte = x % 4;
          var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

          var screenIdx = ScreenRamOffset + cellIndex;
          var screenByte = screenIdx < raw.Length ? raw[screenIdx] : (byte)0;

          var colorIndex = bitValue switch {
            0 => 0,
            1 => (screenByte >> 4) & 0x0F,
            2 => screenByte & 0x0F,
            3 => 0,
            _ => 0
          };

          var color = _C64Palette[colorIndex];
          var offset = (y * width + x) * 3;
          rgb[offset] = (byte)((color >> 16) & 0xFF);
          rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
          rgb[offset + 2] = (byte)(color & 0xFF);
        }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
