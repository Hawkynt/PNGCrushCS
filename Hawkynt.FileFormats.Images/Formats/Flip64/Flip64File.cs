using System;
using FileFormat.Core;

namespace FileFormat.Flip64;

/// <summary>In-memory representation of a C64 Flip interlaced multicolor image.</summary>
public readonly record struct Flip64File
  : IImageFormatReader<Flip64File>, IImageToRawImage<Flip64File>,
    IImageFromRawImage<Flip64File>, IImageFormatWriter<Flip64File> {

  static string IImageFormatMetadata<Flip64File>.PrimaryExtension => ".fbi";
  static string[] IImageFormatMetadata<Flip64File>.FileExtensions => [".fbi"];
  static Flip64File IImageFormatReader<Flip64File>.FromSpan(ReadOnlySpan<byte> data) => Flip64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Flip64File>.ToBytes(Flip64File file) => Flip64Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Flip64File>.VideoModes => [
    new("Default", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of each bitmap data section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Size of each screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size (bitmap1 + screen1 + bitmap2 + screen2 + color).</summary>
  internal const int MinPayloadSize = BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize + ColorRamSize;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Flip image to a platform-independent <see cref="RawImage"/> in Rgb24 format by blending two interlaced frames.</summary>
  public static RawImage ToRawImage(Flip64File file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    const int bitmap2Offset = BitmapSize + ScreenRamSize;
    const int screen1Offset = BitmapSize;
    const int screen2Offset = BitmapSize + ScreenRamSize + BitmapSize;
    const int colorOffset = BitmapSize + ScreenRamSize + BitmapSize + ScreenRamSize;

    var hasFullData = file.RawData.Length >= MinPayloadSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByteOffset = cellIndex * 8 + byteInCell;
        var pixelInByte = x % 4;

        int color1Index, color2Index;

        if (hasFullData) {
          var bitmap1Byte = bitmapByteOffset < file.RawData.Length ? file.RawData[bitmapByteOffset] : (byte)0;
          var bitValue1 = (bitmap1Byte >> ((3 - pixelInByte) * 2)) & 0x03;

          var bitmap2ByteOffset = bitmap2Offset + bitmapByteOffset;
          var bitmap2Byte = bitmap2ByteOffset < file.RawData.Length ? file.RawData[bitmap2ByteOffset] : (byte)0;
          var bitValue2 = (bitmap2Byte >> ((3 - pixelInByte) * 2)) & 0x03;

          var screen1Byte = file.RawData[screen1Offset + cellIndex];
          var screen2Byte = file.RawData[screen2Offset + cellIndex];
          var colorByte = file.RawData[colorOffset + cellIndex];

          color1Index = bitValue1 switch {
            0 => 0,
            1 => (screen1Byte >> 4) & 0x0F,
            2 => screen1Byte & 0x0F,
            3 => colorByte & 0x0F,
            _ => 0
          };

          color2Index = bitValue2 switch {
            0 => 0,
            1 => (screen2Byte >> 4) & 0x0F,
            2 => screen2Byte & 0x0F,
            3 => colorByte & 0x0F,
            _ => 0
          };
        } else {
          var bitmapByte = bitmapByteOffset < file.RawData.Length ? file.RawData[bitmapByteOffset] : (byte)0;
          var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;
          color1Index = bitValue != 0 ? 1 : 0;
          color2Index = color1Index;
        }

        var c1 = Commodore64Graphics.HexColors[color1Index];
        var c2 = Commodore64Graphics.HexColors[color2Index];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((((c1 >> 16) & 0xFF) + ((c2 >> 16) & 0xFF)) / 2);
        rgb[offset + 1] = (byte)((((c1 >> 8) & 0xFF) + ((c2 >> 8) & 0xFF)) / 2);
        rgb[offset + 2] = (byte)(((c1 & 0xFF) + (c2 & 0xFF)) / 2);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds a Flip image from a <see cref="RawImage"/>. Flip's extra colours come from
  /// interlacing two independently-drawn frames, but a single <see cref="RawImage"/> only ever supplies
  /// one picture — encoding it identically into both frames keeps the blended average exact instead of
  /// inventing a second, unrelated frame. Within each frame, pattern 00 is always black (there is no
  /// background register) and only the three next most common colours per 4x8 cell survive, since the
  /// hardware allows just four colours per cell.</summary>
  public static Flip64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Flip images are always {FixedWidth}x{FixedHeight}, but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var bitmap = new byte[BitmapSize];
    var screen = new byte[ScreenRamSize];
    var colorRam = new byte[ColorRamSize];
    const int cellsAcross = 40, cellsDown = 25;

    var cellColors = new int[8, 4];
    Span<int> freq = stackalloc int[16];
    for (var cellY = 0; cellY < cellsDown; ++cellY)
    for (var cellX = 0; cellX < cellsAcross; ++cellX) {
      freq.Clear();
      for (var py = 0; py < 8; ++py)
      for (var px = 0; px < 4; ++px) {
        var x = cellX * 4 + px;
        var y = cellY * 8 + py;
        var o = (y * FixedWidth + x) * 4;
        var color = Commodore64Graphics.FindNearestColorIndex(bgra.PixelData[o + 2], bgra.PixelData[o + 1], bgra.PixelData[o]);
        cellColors[py, px] = color;
        ++freq[color];
      }

      int c1 = 0, c2 = 0, c3 = 0;
      int best1 = -1, best2 = -1, best3 = -1;
      for (var c = 1; c < 16; ++c) {
        if (freq[c] > best1) {
          best3 = best2; c3 = c2;
          best2 = best1; c2 = c1;
          best1 = freq[c]; c1 = c;
        } else if (freq[c] > best2) {
          best3 = best2; c3 = c2;
          best2 = freq[c]; c2 = c;
        } else if (freq[c] > best3) {
          best3 = freq[c]; c3 = c;
        }
      }

      var cellIndex = cellY * cellsAcross + cellX;
      screen[cellIndex] = (byte)((c1 << 4) | c2);
      colorRam[cellIndex] = (byte)c3;

      for (var py = 0; py < 8; ++py) {
        byte rowByte = 0;
        for (var px = 0; px < 4; ++px) {
          var color = cellColors[py, px];
          var pattern = color == 0 ? 0
            : color == c1 ? 1
            : color == c2 ? 2
            : color == c3 ? 3
            : _NearestPatternSlot(color, 0, c1, c2, c3);
          rowByte |= (byte)(pattern << ((3 - px) * 2));
        }

        bitmap[cellIndex * 8 + py] = rowByte;
      }
    }

    var rawData = new byte[MinPayloadSize];
    var offset = 0;
    bitmap.CopyTo(rawData, offset); offset += BitmapSize;
    screen.CopyTo(rawData, offset); offset += ScreenRamSize;
    bitmap.CopyTo(rawData, offset); offset += BitmapSize;
    screen.CopyTo(rawData, offset); offset += ScreenRamSize;
    colorRam.CopyTo(rawData, offset);

    return new() { RawData = rawData };
  }

  private static int _NearestPatternSlot(int color, int p0, int p1, int p2, int p3) {
    Span<int> slots = [p0, p1, p2, p3];
    var target = Commodore64Graphics.HexColors[color];
    int tr = (target >> 16) & 0xFF, tg = (target >> 8) & 0xFF, tb = target & 0xFF;

    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < slots.Length; ++i) {
      var c = Commodore64Graphics.HexColors[slots[i]];
      int dr = ((c >> 16) & 0xFF) - tr, dg = ((c >> 8) & 0xFF) - tg, db = (c & 0xFF) - tb;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;
      bestDistance = distance;
      best = i;
    }

    return best;
  }

}
