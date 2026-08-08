using System;
using FileFormat.Core;

namespace FileFormat.Pixel64;

/// <summary>In-memory representation of a Commodore 64 Pixel Perfect paint image.</summary>
public readonly record struct Pixel64File
  : IImageFormatReader<Pixel64File>, IImageToRawImage<Pixel64File>,
    IImageFromRawImage<Pixel64File>, IImageFormatWriter<Pixel64File> {

  static string IImageFormatMetadata<Pixel64File>.PrimaryExtension => ".px64";
  static string[] IImageFormatMetadata<Pixel64File>.FileExtensions => [".px64", ".px"];
  static Pixel64File IImageFormatReader<Pixel64File>.FromSpan(ReadOnlySpan<byte> data) => Pixel64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Pixel64File>.ToBytes(Pixel64File file) => Pixel64Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Pixel64File>.VideoModes => [
    new("Default", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The fixed width of a Pixel64 image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Pixel64 image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1 + 1 + 14).</summary>
  public const int ExpectedFileSize = 10018;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the padding section in bytes.</summary>
  internal const int PaddingSize = 14;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address, typically 0x2000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Trailing padding bytes (14 bytes).</summary>
  public byte[] Padding { get; init; }

  /// <summary>Converts this Pixel64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Pixel64File file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a Pixel Perfect image from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// VIC-II's fixed sixteen colours; the background register is the picture's most common colour overall,
  /// and within each 4x8 cell only the three next most common colours survive, since the hardware allows
  /// just four colours per cell.</summary>
  public static Pixel64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.SampleTo(FixedWidth, FixedHeight);

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
    var videoMatrix = new byte[VideoMatrixSize];
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
      videoMatrix[cellIndex] = (byte)((c1 << 4) | c2);
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
      BitmapData = bitmap, VideoMatrix = videoMatrix, ColorRam = colorRam,
      BackgroundColor = (byte)background, BorderColor = 0, Padding = new byte[PaddingSize],
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
