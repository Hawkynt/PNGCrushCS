using System;
using FileFormat.Core;

namespace FileFormat.Zoomatic;

/// <summary>In-memory representation of a C64 Zoomatic (.zom) multicolor art image.</summary>
public readonly record struct ZoomaticFile
  : IImageFormatReader<ZoomaticFile>, IImageToRawImage<ZoomaticFile>,
    IImageFromRawImage<ZoomaticFile>, IImageFormatWriter<ZoomaticFile> {

  static string IImageFormatMetadata<ZoomaticFile>.PrimaryExtension => ".zom";
  static string[] IImageFormatMetadata<ZoomaticFile>.FileExtensions => [".zom"];
  static ZoomaticFile IImageFormatReader<ZoomaticFile>.FromSpan(ReadOnlySpan<byte> data) => ZoomaticReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZoomaticFile>.ToBytes(ZoomaticFile file) => ZoomaticWriter.ToBytes(file);

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Default load address, putting the bitmap at $2000.</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

  /// <summary>Minimum raw payload size: bitmap + screen + color.</summary>
  internal const int MinPayloadSize = BitmapDataSize + ScreenDataSize + ColorDataSize; // 10000

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int ImageWidth = 160;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int ImageHeight = 200;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM / video matrix (1000 bytes).</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorData { get; init; }

  /// <summary>Background color index (0-15). Bit-pair 0 maps to this color.</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Any trailing bytes beyond the minimum payload.</summary>
  public byte[] TrailingData { get; init; }

  /// <summary>Converts this Zoomatic image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ZoomaticFile file) {

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitPair = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitPair switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenData[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenData[cellIndex] & 0x0F,
          3 => file.ColorData[cellIndex] & 0x0F,
          _ => 0
        };

        var color = Commodore64Graphics.HexColors[colorIndex];
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


  /// <summary>Encodes a picture as a Zoomatic screen, scaling it to 160x200 first.</summary>
  /// <remarks>
  /// The background register is shared by the whole screen, so it goes to the picture's commonest
  /// colour and every cell keeps all three of its own entries for what sets it apart.
  /// </remarks>
  public static ZoomaticFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(ImageWidth, ImageHeight).PixelData;
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenDataSize];
    var color = new byte[ColorDataSize];
    var background = Commodore64Graphics.EncodeMulticolor(rgb, ImageWidth, ImageHeight, bitmap, screen, color);

    return new() {
      LoadAddress = DefaultLoadAddress,
      BitmapData = bitmap,
      ScreenData = screen,
      ColorData = color,
      BackgroundColor = background,
      TrailingData = [],
    };
  }

}
