using System;
using FileFormat.Core;

namespace FileFormat.ChampionsInterlace;

/// <summary>In-memory representation of a C64 Champions Interlace (.cin) multicolor interlace image.</summary>
public readonly record struct ChampionsInterlaceFile
  : IImageFormatReader<ChampionsInterlaceFile>, IImageToRawImage<ChampionsInterlaceFile>,
    IImageFromRawImage<ChampionsInterlaceFile>, IImageFormatWriter<ChampionsInterlaceFile> {

  static string IImageFormatMetadata<ChampionsInterlaceFile>.PrimaryExtension => ".cin";
  static string[] IImageFormatMetadata<ChampionsInterlaceFile>.FileExtensions => [".cin"];
  static ChampionsInterlaceFile IImageFormatReader<ChampionsInterlaceFile>.FromSpan(ReadOnlySpan<byte> data) => ChampionsInterlaceReader.FromSpan(data);
  static byte[] IImageFormatWriter<ChampionsInterlaceFile>.ToBytes(ChampionsInterlaceFile file) => ChampionsInterlaceWriter.ToBytes(file);

  /// <summary>Image width in pixels, always 160 (multicolor).</summary>
  public const int ImageWidth = 160;

  /// <summary>Image height in pixels, always 200.</summary>
  public const int ImageHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of a single bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of a single screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorDataSize = 1000;

  /// <summary>Total file size: LoadAddress(2) + Bitmap1(8000) + Screen1(1000) + ColorData(1000) + Bitmap2(8000) + Screen2(1000) + BackgroundColor(1) = 19003.</summary>
  public const int FileSize = 19003;

  /// <summary>Minimum payload size (everything except load address).</summary>
  internal const int MinPayloadSize = FileSize - LoadAddressSize; // 19001

  /// <summary>Default load address, the one the program itself writes.</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>First frame bitmap data (8000 bytes).</summary>
  public byte[] Bitmap1 { get; init; }

  /// <summary>First frame screen RAM (1000 bytes).</summary>
  public byte[] Screen1 { get; init; }

  /// <summary>Shared color RAM (1000 bytes).</summary>
  public byte[] ColorData { get; init; }

  /// <summary>Second frame bitmap data (8000 bytes).</summary>
  public byte[] Bitmap2 { get; init; }

  /// <summary>Second frame screen RAM (1000 bytes).</summary>
  public byte[] Screen2 { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Champions Interlace image to a platform-independent <see cref="RawImage"/> in Rgb24 format by averaging both multicolor frames.</summary>
  public static RawImage ToRawImage(ChampionsInterlaceFile file) {

    const int width = ImageWidth;
    const int height = ImageHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var color1 = _DecodeMulticolorPixel(file.Bitmap1, file.Screen1, file.ColorData, file.BackgroundColor, x, y);
        var color2 = _DecodeMulticolorPixel(file.Bitmap2, file.Screen2, file.ColorData, file.BackgroundColor, x, y);

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

  /// <summary>Decodes a single multicolor pixel from the given frame data.</summary>
  private static int _DecodeMulticolorPixel(byte[] bitmapData, byte[] screenData, byte[] colorData, byte backgroundColor, int x, int y) {
    var cellX = x / 4;
    var cellY = y / 8;
    var cellIndex = cellY * 40 + cellX;
    var byteInCell = y % 8;

    var bitmapOffset = cellIndex * 8 + byteInCell;
    var bitmapByte = bitmapOffset < bitmapData.Length ? bitmapData[bitmapOffset] : (byte)0;
    var pixelInByte = x % 4;
    var bitPair = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

    var colorIndex = bitPair switch {
      0 => backgroundColor & 0x0F,
      1 => cellIndex < screenData.Length ? (screenData[cellIndex] >> 4) & 0x0F : 0,
      2 => cellIndex < screenData.Length ? screenData[cellIndex] & 0x0F : 0,
      3 => cellIndex < colorData.Length ? colorData[cellIndex] & 0x0F : 0,
      _ => 0
    };

    return Commodore64Graphics.HexColors[colorIndex];
  }


  /// <summary>Encodes a picture as a Champions Interlace pair, scaling it to 160x200 first.</summary>
  /// <remarks>
  /// The fields share one colour memory and one background register and differ only in bitmap and
  /// video matrix. Both are given the same contents: <see cref="ToRawImage"/> reports the average of
  /// the two, so only a matching pair averages back to the picture handed in.
  /// </remarks>
  public static ChampionsInterlaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(ImageWidth, ImageHeight).PixelData;
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenDataSize];
    var color = new byte[ColorDataSize];
    var background = Commodore64Graphics.EncodeMulticolor(rgb, ImageWidth, ImageHeight, bitmap, screen, color);

    return new() {
      LoadAddress = DefaultLoadAddress,
      Bitmap1 = bitmap,
      Screen1 = screen,
      ColorData = color,
      Bitmap2 = bitmap[..],
      Screen2 = screen[..],
      BackgroundColor = background,
    };
  }

}
