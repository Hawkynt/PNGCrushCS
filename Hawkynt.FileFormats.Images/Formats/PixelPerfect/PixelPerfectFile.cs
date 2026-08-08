using System;
using FileFormat.Core;

namespace FileFormat.PixelPerfect;

/// <summary>In-memory representation of a Commodore 64 Pixel Perfect multicolor image.</summary>
public readonly record struct PixelPerfectFile : IImageFormatReader<PixelPerfectFile>, IImageToRawImage<PixelPerfectFile>, IImageFromRawImage<PixelPerfectFile>, IImageFormatWriter<PixelPerfectFile> {

  static string IImageFormatMetadata<PixelPerfectFile>.PrimaryExtension => ".pp";
  static string[] IImageFormatMetadata<PixelPerfectFile>.FileExtensions => [".pp", ".ppp"];
  static PixelPerfectFile IImageFormatReader<PixelPerfectFile>.FromSpan(ReadOnlySpan<byte> data) => PixelPerfectReader.FromSpan(data);
  static byte[] IImageFormatWriter<PixelPerfectFile>.ToBytes(PixelPerfectFile file) => PixelPerfectWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum bitmap data size in the payload.</summary>
  internal const int MinBitmapSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Standard payload size for .pp files (bitmap + screen RAM).</summary>
  internal const int StandardPayloadSize = MinBitmapSize + ScreenRamSize;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Pixel Perfect image to a platform-independent <see cref="RawImage"/> in Rgb24 format using multicolor decode.</summary>
  public static RawImage ToRawImage(PixelPerfectFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    var hasScreen = file.RawData.Length >= MinBitmapSize + ScreenRamSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapOffset = cellIndex * 8 + byteInCell;
        var bitmapByte = bitmapOffset < file.RawData.Length ? file.RawData[bitmapOffset] : (byte)0;
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        int colorIndex;
        if (hasScreen) {
          var screenByte = file.RawData[MinBitmapSize + cellIndex];

          colorIndex = bitValue switch {
            0 => 0,
            1 => (screenByte >> 4) & 0x0F,
            2 => screenByte & 0x0F,
            3 => 0,
            _ => 0
          };
        } else
          colorIndex = bitValue != 0 ? 1 : 0;

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

  /// <summary>Creates a Pixel Perfect screen from a <see cref="RawImage"/>, sampling it to the VIC-II's 160x200 multicolour screen.</summary>
  /// <remarks>
  /// The payload is bitmap plus screen RAM and nothing else: there is no colour RAM and no
  /// background register, so <see cref="ToRawImage"/> reads bit patterns 00 and 11 alike as black
  /// and only 01 and 10 name a colour. That leaves three colours per 4x8 cell — black plus the two
  /// the screen byte's nibbles hold — and this encoder gives each cell its two commonest non-black
  /// colours. Pattern 11 is never written, since it would decode as black anyway.
  /// </remarks>
  public static PixelPerfectFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = image.SampleTo(FixedWidth, FixedHeight).ToBgra32();
    var indices = new byte[FixedWidth * FixedHeight];
    for (var i = 0; i < indices.Length; ++i) {
      var offset = i * 4;
      indices[i] = (byte)Commodore64Graphics.FindNearestColorIndex(bgra[offset + 2], bgra[offset + 1], bgra[offset]);
    }

    var rawData = new byte[StandardPayloadSize];
    Span<int> cellColors = stackalloc int[2];

    for (var cellY = 0; cellY < FixedHeight / 8; ++cellY)
      for (var cellX = 0; cellX < 40; ++cellX) {
        var cellIndex = cellY * 40 + cellX;
        _PickCellColors(indices, cellX, cellY, cellColors);
        rawData[MinBitmapSize + cellIndex] = (byte)((cellColors[0] << 4) | cellColors[1]);

        for (var row = 0; row < 8; ++row) {
          byte packed = 0;
          for (var column = 0; column < 4; ++column) {
            var color = indices[(cellY * 8 + row) * FixedWidth + cellX * 4 + column];
            var pattern = _PickPattern(color, cellColors);
            packed |= (byte)(pattern << ((3 - column) * 2));
          }

          rawData[cellIndex * 8 + row] = packed;
        }
      }

    return new() {
      LoadAddress = 0x2000,
      RawData = rawData,
    };
  }

  /// <summary>Fills <paramref name="cellColors"/> with the two commonest colours in the cell that are not black.</summary>
  private static void _PickCellColors(byte[] indices, int cellX, int cellY, Span<int> cellColors) {
    Span<int> frequency = stackalloc int[16];
    for (var row = 0; row < 8; ++row)
      for (var column = 0; column < 4; ++column)
        ++frequency[indices[(cellY * 8 + row) * FixedWidth + cellX * 4 + column]];

    frequency[0] = -1;
    for (var slot = 0; slot < 2; ++slot) {
      var best = 0;
      for (var i = 1; i < 16; ++i)
        if (frequency[i] > frequency[best])
          best = i;

      cellColors[slot] = frequency[best] > 0 ? best : 0;
      if (frequency[best] > 0)
        frequency[best] = -1;
    }
  }

  /// <summary>Picks the two-bit pattern whose nibble holds the colour, or the nearest colour the cell does hold.</summary>
  private static int _PickPattern(byte color, ReadOnlySpan<int> cellColors) {
    if (color == 0)
      return 0;

    for (var slot = 0; slot < 2; ++slot)
      if (cellColors[slot] == color)
        return slot + 1;

    var bestPattern = 0;
    var bestDistance = _Distance(color, 0);
    for (var slot = 0; slot < 2; ++slot) {
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
