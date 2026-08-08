using System;
using FileFormat.Core;

namespace FileFormat.TruePaint;

/// <summary>In-memory representation of a True Paint interlace multicolor image (.mci).</summary>
public readonly record struct TruePaintFile : IImageFormatReader<TruePaintFile>, IImageToRawImage<TruePaintFile>, IImageFromRawImage<TruePaintFile>, IImageFormatWriter<TruePaintFile> {

  static string IImageFormatMetadata<TruePaintFile>.PrimaryExtension => ".mci";
  static string[] IImageFormatMetadata<TruePaintFile>.FileExtensions => [".mci"];
  static TruePaintFile IImageFormatReader<TruePaintFile>.FromSpan(ReadOnlySpan<byte> data) => TruePaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<TruePaintFile>.ToBytes(TruePaintFile file) => TruePaintWriter.ToBytes(file);

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

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian, typically $9C00).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>First multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData1 { get; init; }

  /// <summary>First screen RAM (1000 bytes).</summary>
  public byte[] ScreenRam1 { get; init; }

  /// <summary>Second multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData2 { get; init; }

  /// <summary>Second screen RAM (1000 bytes).</summary>
  public byte[] ScreenRam2 { get; init; }

  /// <summary>Color RAM shared by both bitmaps (1000 bytes).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Converts this True Paint image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(TruePaintFile file) {

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

        var color1 = Commodore64Graphics.HexColors[colorIndex1];
        var color2 = Commodore64Graphics.HexColors[colorIndex2];

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

  /// <summary>Creates a True Paint picture from a <see cref="RawImage"/>, sampling it to the VIC-II's 160x200 multicolour screen.</summary>
  /// <remarks>
  /// True Paint holds two multicolour screens that the machine alternates between, and
  /// <see cref="ToRawImage"/> reproduces that by averaging the two. Writing the same screen into
  /// both fields is what a still picture wants: the average of a colour with itself is that colour,
  /// so what comes back is exactly what went in. Mixing two different fields would buy extra
  /// apparent colours at the cost of never reproducing the original, which is a dithering decision
  /// and not one an encoder should make silently.
  /// <para/>
  /// Within one field the hardware allows four colours per 4x8 cell: a shared background register
  /// plus the two screen nibbles and the colour RAM nibble.
  /// </remarks>
  public static TruePaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = image.SampleTo(FixedWidth, FixedHeight).ToBgra32();
    var indices = new byte[FixedWidth * FixedHeight];
    for (var i = 0; i < indices.Length; ++i) {
      var offset = i * 4;
      indices[i] = (byte)Commodore64Graphics.FindNearestColorIndex(bgra[offset + 2], bgra[offset + 1], bgra[offset]);
    }

    Span<int> frequency = stackalloc int[16];
    foreach (var index in indices)
      ++frequency[index];

    var background = 0;
    for (var i = 1; i < 16; ++i)
      if (frequency[i] > frequency[background])
        background = i;

    var bitmapData = new byte[BitmapDataSize];
    var screenRam = new byte[ScreenRamSize];
    var colorRam = new byte[ColorRamSize];
    Span<int> cellColors = stackalloc int[3];

    for (var cellY = 0; cellY < FixedHeight / 8; ++cellY)
      for (var cellX = 0; cellX < 40; ++cellX) {
        var cellIndex = cellY * 40 + cellX;
        _PickCellColors(indices, cellX, cellY, (byte)background, cellColors);

        screenRam[cellIndex] = (byte)((cellColors[0] << 4) | cellColors[1]);
        colorRam[cellIndex] = (byte)cellColors[2];

        for (var row = 0; row < 8; ++row) {
          byte packed = 0;
          for (var column = 0; column < 4; ++column) {
            var color = indices[(cellY * 8 + row) * FixedWidth + cellX * 4 + column];
            var pattern = _PickPattern(color, (byte)background, cellColors);
            packed |= (byte)(pattern << ((3 - column) * 2));
          }

          bitmapData[cellIndex * 8 + row] = packed;
        }
      }

    return new() {
      LoadAddress = 0x9C00,
      BitmapData1 = bitmapData,
      ScreenRam1 = screenRam,
      BitmapData2 = bitmapData[..],
      ScreenRam2 = screenRam[..],
      ColorRam = colorRam,
      BackgroundColor = (byte)background,
      BorderColor = (byte)background,
    };
  }

  /// <summary>Fills <paramref name="cellColors"/> with the three commonest colours in the cell that are not the background.</summary>
  private static void _PickCellColors(byte[] indices, int cellX, int cellY, byte background, Span<int> cellColors) {
    Span<int> frequency = stackalloc int[16];
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 4; ++column)
        ++frequency[indices[(cellY * 8 + row) * FixedWidth + cellX * 4 + column]];

    frequency[background] = -1;
    for (var slot = 0; slot < 3; ++slot) {
      var best = 0;
      for (var i = 1; i < 16; ++i)
        if (frequency[i] > frequency[best])
          best = i;

      cellColors[slot] = frequency[best] > 0 ? best : background;
      if (frequency[best] > 0)
        frequency[best] = -1;
    }
  }

  /// <summary>Picks the two-bit pattern whose register holds the colour, or the nearest one it does hold.</summary>
  private static int _PickPattern(byte color, byte background, ReadOnlySpan<int> cellColors) {
    if (color == background)
      return 0;

    for (var slot = 0; slot < 3; ++slot)
      if (cellColors[slot] == color)
        return slot + 1;

    var bestPattern = 0;
    var bestDistance = _Distance(color, background);
    for (var slot = 0; slot < 3; ++slot) {
      var distance = _Distance(color, (byte)cellColors[slot]);
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      bestPattern = slot + 1;
    }

    return bestPattern;
  }

  /// <summary>Squared RGB distance between two VIC-II palette entries.</summary>
  private static int _Distance(byte left, byte right) {
    var a = Commodore64Graphics.HexColors[left];
    var b = Commodore64Graphics.HexColors[right];
    var dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    var dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    var db = (a & 0xFF) - (b & 0xFF);
    return dr * dr + dg * dg + db * db;
  }

}
