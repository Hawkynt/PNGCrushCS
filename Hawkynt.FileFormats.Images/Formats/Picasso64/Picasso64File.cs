using System;
using FileFormat.Core;

namespace FileFormat.Picasso64;

/// <summary>In-memory representation of a Commodore 64 Picasso 64 multicolor image.</summary>
public readonly record struct Picasso64File
  : IImageFormatReader<Picasso64File>, IImageToRawImage<Picasso64File>,
    IImageFromRawImage<Picasso64File>, IImageFormatWriter<Picasso64File> {

  static string IImageFormatMetadata<Picasso64File>.PrimaryExtension => ".p64";
  static string[] IImageFormatMetadata<Picasso64File>.FileExtensions => [".p64"];
  static Picasso64File IImageFormatReader<Picasso64File>.FromSpan(ReadOnlySpan<byte> data) => Picasso64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Picasso64File>.ToBytes(Picasso64File file) => Picasso64Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Picasso64File>.VideoModes => [
    new("Default", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The fixed width of a Picasso 64 image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Picasso 64 image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1 + 1 + 46).</summary>
  public const int ExpectedFileSize = 10050;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the extra data section in bytes.</summary>
  internal const int ExtraDataSize = 46;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] ScreenRam { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Extra data (46 bytes of format-specific metadata).</summary>
  public byte[] ExtraData { get; init; }

  /// <summary>Converts this Picasso 64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Picasso64File file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenRam[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenRam[cellIndex] & 0x0F,
          3 => file.ColorRam[cellIndex] & 0x0F,
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

  /// <summary>Builds a Picasso 64 image from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// VIC-II's fixed sixteen colours; the background register is the picture's most common colour overall,
  /// and within each 4x8 cell only the three next most common colours survive, since the hardware allows
  /// just four colours per cell.</summary>
  public static Picasso64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Picasso 64 images are always {FixedWidth}x{FixedHeight}, but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    const int cellsAcross = 40, cellsDown = 25;
    var colorAt = new int[FixedHeight, FixedWidth];

    Span<int> globalFreq = stackalloc int[16];
    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < FixedWidth; ++x) {
      var o = (y * FixedWidth + x) * 4;
      var color = Commodore64Graphics.FindNearestColorIndex(bgra.PixelData[o + 2], bgra.PixelData[o + 1], bgra.PixelData[o]);
      colorAt[y, x] = color;
      ++globalFreq[color];
    }

    var background = 0;
    for (var c = 1; c < 16; ++c)
      if (globalFreq[c] > globalFreq[background])
        background = c;

    var bitmap = new byte[BitmapDataSize];
    var screenRam = new byte[ScreenRamSize];
    var colorRam = new byte[ColorRamSize];

    Span<int> cellFreq = stackalloc int[16];
    for (var cellY = 0; cellY < cellsDown; ++cellY)
    for (var cellX = 0; cellX < cellsAcross; ++cellX) {
      cellFreq.Clear();
      for (var py = 0; py < 8; ++py)
      for (var px = 0; px < 4; ++px) {
        var color = colorAt[cellY * 8 + py, cellX * 4 + px];
        if (color != background)
          ++cellFreq[color];
      }

      int c1 = 0, c2 = 0, c3 = 0;
      int best1 = -1, best2 = -1, best3 = -1;
      for (var c = 0; c < 16; ++c) {
        if (c == background)
          continue;
        if (cellFreq[c] > best1) {
          best3 = best2; c3 = c2;
          best2 = best1; c2 = c1;
          best1 = cellFreq[c]; c1 = c;
        } else if (cellFreq[c] > best2) {
          best3 = best2; c3 = c2;
          best2 = cellFreq[c]; c2 = c;
        } else if (cellFreq[c] > best3) {
          best3 = cellFreq[c]; c3 = c;
        }
      }

      var cellIndex = cellY * cellsAcross + cellX;
      screenRam[cellIndex] = (byte)((c1 << 4) | c2);
      colorRam[cellIndex] = (byte)c3;

      for (var py = 0; py < 8; ++py) {
        byte rowByte = 0;
        for (var px = 0; px < 4; ++px) {
          var color = colorAt[cellY * 8 + py, cellX * 4 + px];
          var pattern = color == background ? 0
            : color == c1 ? 1
            : color == c2 ? 2
            : color == c3 ? 3
            : _NearestPatternSlot(color, background, c1, c2, c3);
          rowByte |= (byte)(pattern << ((3 - px) * 2));
        }

        bitmap[cellIndex * 8 + py] = rowByte;
      }
    }

    return new() {
      BitmapData = bitmap, ScreenRam = screenRam, ColorRam = colorRam,
      BackgroundColor = (byte)background, BorderColor = 0, ExtraData = new byte[ExtraDataSize],
    };
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
