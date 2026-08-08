using System;
using FileFormat.Core;

namespace FileFormat.SuperHires;

/// <summary>In-memory representation of a Super Hires (C64 interlace hires) image.</summary>
public readonly record struct SuperHiresFile : IImageFormatReader<SuperHiresFile>, IImageToRawImage<SuperHiresFile>, IImageFromRawImage<SuperHiresFile>, IImageFormatWriter<SuperHiresFile> {

  static string IImageFormatMetadata<SuperHiresFile>.PrimaryExtension => ".shi";
  static string[] IImageFormatMetadata<SuperHiresFile>.FileExtensions => [".shi"];
  static SuperHiresFile IImageFormatReader<SuperHiresFile>.FromSpan(ReadOnlySpan<byte> data) => SuperHiresReader.FromSpan(data);
  static byte[] IImageFormatWriter<SuperHiresFile>.ToBytes(SuperHiresFile file) => SuperHiresWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public const int ImageWidth = 320;

  /// <summary>Image height in pixels.</summary>
  public const int ImageHeight = 200;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the padding/extra data at the end.</summary>
  internal const int PaddingSize = 240;

  /// <summary>Expected file size: 2 + 8000 + 1000 + 8000 + 1000 + 240 = 18242.</summary>
  public const int ExpectedFileSize = LoadAddressSize + BitmapDataSize + ScreenDataSize + BitmapDataSize + ScreenDataSize + PaddingSize;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data for frame 1 (8000 bytes).</summary>
  public byte[] BitmapData1 { get; init; }

  /// <summary>Screen RAM for frame 1 (1000 bytes).</summary>
  public byte[] ScreenData1 { get; init; }

  /// <summary>Bitmap data for frame 2 (8000 bytes).</summary>
  public byte[] BitmapData2 { get; init; }

  /// <summary>Screen RAM for frame 2 (1000 bytes).</summary>
  public byte[] ScreenData2 { get; init; }

  /// <summary>Trailing padding/extra data.</summary>
  public byte[] Padding { get; init; }

  /// <summary>Converts this Super Hires image to a platform-independent <see cref="RawImage"/> in Rgb24 format by blending two interlace frames.</summary>
  public static RawImage ToRawImage(SuperHiresFile file) {

    var rgb = new byte[ImageWidth * ImageHeight * 3];

    for (var y = 0; y < ImageHeight; ++y)
      for (var x = 0; x < ImageWidth; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitPosition = 7 - (x % 8);

        // Decode frame 1
        var bitmapByte1 = file.BitmapData1[cellIndex * 8 + byteInCell];
        var bitValue1 = (bitmapByte1 >> bitPosition) & 1;
        var screenByte1 = file.ScreenData1[cellIndex];
        var colorIndex1 = bitValue1 == 1
          ? (screenByte1 >> 4) & 0x0F
          : screenByte1 & 0x0F;

        // Decode frame 2
        var bitmapByte2 = file.BitmapData2[cellIndex * 8 + byteInCell];
        var bitValue2 = (bitmapByte2 >> bitPosition) & 1;
        var screenByte2 = file.ScreenData2[cellIndex];
        var colorIndex2 = bitValue2 == 1
          ? (screenByte2 >> 4) & 0x0F
          : screenByte2 & 0x0F;

        var color1 = Commodore64Graphics.HexColors[colorIndex1];
        var color2 = Commodore64Graphics.HexColors[colorIndex2];

        // Combine: same color = solid, different = average
        int r, g, b;
        if (colorIndex1 == colorIndex2) {
          r = (color1 >> 16) & 0xFF;
          g = (color1 >> 8) & 0xFF;
          b = color1 & 0xFF;
        } else {
          r = (((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2;
          g = (((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2;
          b = ((color1 & 0xFF) + (color2 & 0xFF)) / 2;
        }

        var offset = (y * ImageWidth + x) * 3;
        rgb[offset] = (byte)r;
        rgb[offset + 1] = (byte)g;
        rgb[offset + 2] = (byte)b;
      }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds an interlace picture from any image, sampling it to the 320x200 high-resolution screen.</summary>
  /// <remarks>
  /// Both fields are given the same screen. Interlacing buys colours the machine cannot otherwise
  /// show, but only ones that are the exact average of two of its sixteen — anything else the eye
  /// merely tolerates. A field that differs from its partner therefore has to be paid for in flicker
  /// on real hardware, and the decoder above shows the two averaged rather than alternating, so a
  /// picture already made of the machine's own colours gains nothing from the difference and loses
  /// exactness: written twice the same, it comes back the colours it went in as.
  /// </remarks>
  public static SuperHiresFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(ImageWidth, ImageHeight).EnsureFormat(PixelFormat.Rgb24);
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenDataSize];
    Commodore64Graphics.EncodeHires(rgb.PixelData, ImageWidth, ImageHeight, bitmap, screen);

    return new() {
      // Where the VIC-II expects a bitmap screen to sit.
      LoadAddress = 0x2000,
      BitmapData1 = bitmap,
      ScreenData1 = screen,
      BitmapData2 = (byte[])bitmap.Clone(),
      ScreenData2 = (byte[])screen.Clone(),
      Padding = new byte[PaddingSize],
    };
  }

}
