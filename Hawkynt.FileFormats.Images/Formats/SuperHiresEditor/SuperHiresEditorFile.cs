using System;
using FileFormat.Core;

namespace FileFormat.SuperHiresEditor;

/// <summary>In-memory representation of a C64 Super Hires Editor (.she) interlace hires image.</summary>
public readonly record struct SuperHiresEditorFile : IImageFormatReader<SuperHiresEditorFile>, IImageToRawImage<SuperHiresEditorFile>, IImageFromRawImage<SuperHiresEditorFile>, IImageFormatWriter<SuperHiresEditorFile> {

  static string IImageFormatMetadata<SuperHiresEditorFile>.PrimaryExtension => ".she";
  static string[] IImageFormatMetadata<SuperHiresEditorFile>.FileExtensions => [".she"];
  static SuperHiresEditorFile IImageFormatReader<SuperHiresEditorFile>.FromSpan(ReadOnlySpan<byte> data) => SuperHiresEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<SuperHiresEditorFile>.ToBytes(SuperHiresEditorFile file) => SuperHiresEditorWriter.ToBytes(file);

  /// <summary>Size of one bitmap section in bytes (320x200 / 8 = 8000).</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of one screen RAM section in bytes (40x25 = 1000).</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum raw payload size: bitmap1 + screen1 + bitmap2 + screen2.</summary>
  internal const int MinPayloadSize = BitmapDataSize + ScreenDataSize + BitmapDataSize + ScreenDataSize; // 18000

  /// <summary>Image width in pixels, always 320.</summary>
  public const int ImageWidth = 320;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int ImageHeight = 200;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data for frame 1 (8000 bytes).</summary>
  public byte[] Bitmap1 { get; init; }

  /// <summary>Screen RAM for frame 1 (1000 bytes).</summary>
  public byte[] Screen1 { get; init; }

  /// <summary>Bitmap data for frame 2 (8000 bytes).</summary>
  public byte[] Bitmap2 { get; init; }

  /// <summary>Screen RAM for frame 2 (1000 bytes).</summary>
  public byte[] Screen2 { get; init; }

  /// <summary>Any trailing bytes beyond the minimum payload.</summary>
  public byte[] TrailingData { get; init; }

  /// <summary>Converts this Super Hires Editor image to a platform-independent <see cref="RawImage"/> in Rgb24 format by averaging the two interlace frames.</summary>
  public static RawImage ToRawImage(SuperHiresEditorFile file) {

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var color1 = _DecodeHiresPixel(file.Bitmap1, file.Screen1, x, y);
        var color2 = _DecodeHiresPixel(file.Bitmap2, file.Screen2, x, y);

        // Average the two frames for interlace blending
        var r = ((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF);
        var g = ((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF);
        var b = (color1 & 0xFF) + (color2 & 0xFF);

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)(r / 2);
        rgb[offset + 1] = (byte)(g / 2);
        rgb[offset + 2] = (byte)(b / 2);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds an interlace picture from any image, sampling it to the 320x200 high-resolution screen.</summary>
  /// <remarks>
  /// Both fields are given the same screen. The decoder above averages them rather than alternating,
  /// and the only colours an average adds are the exact midpoints of two of the machine's sixteen —
  /// so a picture already drawn in those sixteen gains nothing by differing and comes back exactly
  /// as it went in when the two agree.
  /// </remarks>
  public static SuperHiresEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(ImageWidth, ImageHeight).EnsureFormat(PixelFormat.Rgb24);
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenDataSize];
    Commodore64Graphics.EncodeHires(rgb.PixelData, ImageWidth, ImageHeight, bitmap, screen);

    return new() {
      // Where the VIC-II expects a bitmap screen to sit.
      LoadAddress = 0x2000,
      Bitmap1 = bitmap,
      Screen1 = screen,
      Bitmap2 = (byte[])bitmap.Clone(),
      Screen2 = (byte[])screen.Clone(),
      TrailingData = [],
    };
  }

  /// <summary>Decodes a single hires pixel from bitmap + screen data and returns the C64 palette color as 0xRRGGBB.</summary>
  private static int _DecodeHiresPixel(byte[] bitmap, byte[] screen, int x, int y) {
    var cellX = x / 8;
    var cellY = y / 8;
    var cellIndex = cellY * 40 + cellX;
    var byteInCell = y % 8;
    var bitmapByte = bitmap[cellIndex * 8 + byteInCell];
    var bitPosition = 7 - (x % 8);
    var bitValue = (bitmapByte >> bitPosition) & 1;

    var screenByte = screen[cellIndex];
    var colorIndex = bitValue == 1
      ? (screenByte >> 4) & 0x0F
      : screenByte & 0x0F;

    return Commodore64Graphics.HexColors[colorIndex];
  }
}
