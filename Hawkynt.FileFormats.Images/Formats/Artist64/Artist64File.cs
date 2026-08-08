using System;
using FileFormat.Core;

namespace FileFormat.Artist64;

/// <summary>In-memory representation of a Commodore 64 Artist 64 multicolor image.</summary>
public readonly record struct Artist64File
  : IImageFormatReader<Artist64File>, IImageToRawImage<Artist64File>,
    IImageFromRawImage<Artist64File>, IImageFormatWriter<Artist64File> {

  static string IImageFormatMetadata<Artist64File>.PrimaryExtension => ".a64";
  static string[] IImageFormatMetadata<Artist64File>.FileExtensions => [".a64"];
  static Artist64File IImageFormatReader<Artist64File>.FromSpan(ReadOnlySpan<byte> data) => Artist64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Artist64File>.ToBytes(Artist64File file) => Artist64Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Artist64File>.VideoModes => [
    new("Default", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The fixed width of an Artist 64 image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of an Artist 64 image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 240).</summary>
  public const int ExpectedFileSize = 10242;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the trailing padding in bytes.</summary>
  internal const int PaddingSize = 240;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Converts this Artist 64 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Artist64File file)
    => Commodore64Graphics.DecodeMulticolor(
      // Pattern 00 is always black here: neither format stores a background register.
      file.BitmapData, file.VideoMatrix, file.ColorRam, 0, FixedWidth, FixedHeight);

  /// <summary>Builds an Artist 64 image from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// VIC-II's fixed sixteen colours; within each 4x8 cell, pattern 00 always renders black (the format
  /// has no background register) and only the three next most common colours survive in the other
  /// three pattern slots, since the hardware allows just four colours per cell.</summary>
  public static Artist64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Artist 64 images are always {FixedWidth}x{FixedHeight}, but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var bitmap = new byte[BitmapDataSize];
    var videoMatrix = new byte[VideoMatrixSize];
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

      videoMatrix[cellY * cellsAcross + cellX] = (byte)((c1 << 4) | c2);
      colorRam[cellY * cellsAcross + cellX] = (byte)c3;

      for (var py = 0; py < 8; ++py) {
        byte rowByte = 0;
        for (var px = 0; px < 4; ++px) {
          var color = cellColors[py, px];
          var pattern = color == 0 ? 0
            : color == c1 ? 1
            : color == c2 ? 2
            : color == c3 ? 3
            : _NearestPatternSlot(color, c1, c2, c3);
          rowByte |= (byte)(pattern << ((3 - px) * 2));
        }

        bitmap[(cellY * cellsAcross + cellX) * 8 + py] = rowByte;
      }
    }

    return new() { BitmapData = bitmap, VideoMatrix = videoMatrix, ColorRam = colorRam };
  }

  /// <summary>Chooses which of the four available pattern colours (0=black, 1=<paramref name="c1"/>,
  /// 2=<paramref name="c2"/>, 3=<paramref name="c3"/>) is closest to <paramref name="color"/>, for pixels
  /// whose quantized colour didn't make the cell's cut.</summary>
  private static int _NearestPatternSlot(int color, int c1, int c2, int c3) {
    Span<int> slots = [0, c1, c2, c3];
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
