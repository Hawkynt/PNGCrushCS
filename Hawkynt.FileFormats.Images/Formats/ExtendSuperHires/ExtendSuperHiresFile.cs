using System;
using FileFormat.Core;

namespace FileFormat.ExtendSuperHires;

/// <summary>In-memory representation of an Extend Super Hires Interlace Editor picture (.esh).</summary>
/// <remarks>
/// Two C64 hires screens shown alternately and averaged, each with a band of sprites over it. Both
/// share one colour map and one set of sprite colours: interlacing on this machine buys the
/// mixtures between colours the cells already have, so duplicating the colour data would cost half
/// the file for nothing.
/// <para/>
/// A sprite covers three character columns and twenty-one rows, and the picture is 192 by 200 —
/// eight sprites across and just under ten down, which is what a C64 can display without the
/// multiplexing that would cost the processor.
/// </remarks>
public readonly record struct ExtendSuperHiresFile
  : IImageFormatReader<ExtendSuperHiresFile>, IImageToRawImage<ExtendSuperHiresFile>,
    IImageFromRawImage<ExtendSuperHiresFile>, IImageFormatWriter<ExtendSuperHiresFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 192;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Offset of the first frame's bitmap.</summary>
  public const int FirstBitmapOffset = 3;

  /// <summary>Offset of the second frame's bitmap.</summary>
  public const int SecondBitmapOffset = 4803;

  /// <summary>Offset of the first frame's sprites.</summary>
  public const int FirstSpriteOffset = 9603;

  /// <summary>Offset of the second frame's sprites.</summary>
  public const int SecondSpriteOffset = 14723;

  /// <summary>Offset of the colour map both frames share.</summary>
  public const int ColorMapOffset = 19843;

  /// <summary>Offset of the sprite colours, one per three columns.</summary>
  public const int SpriteColorsOffset = 20443;

  /// <summary>Size of a file that carries the picture outright.</summary>
  public const int UnpackedFileSize = 20454;

  /// <summary>Size a packed file unpacks to.</summary>
  public const int UnpackedSize = 20452;

  static string IImageFormatMetadata<ExtendSuperHiresFile>.PrimaryExtension => ".esh";
  static string[] IImageFormatMetadata<ExtendSuperHiresFile>.FileExtensions => [".esh"];
  static ExtendSuperHiresFile IImageFormatReader<ExtendSuperHiresFile>.FromSpan(ReadOnlySpan<byte> data)
    => ExtendSuperHiresReader.FromSpan(data);
  static byte[] IImageFormatWriter<ExtendSuperHiresFile>.ToBytes(ExtendSuperHiresFile file)
    => ExtendSuperHiresWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ExtendSuperHiresFile>.VideoModes => [
    new("Extend Super Hires", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture, unpacked if it was packed.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(ExtendSuperHiresFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();

    var first = _Render(data, FirstBitmapOffset, FirstSpriteOffset, palette);
    var second = _Render(data, SecondBitmapOffset, SecondSpriteOffset, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _Render(ReadOnlySpan<byte> data, int bitmap, int sprites, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var bit = ~x & 7;
      var column = x >> 3;

      int color;
      var sprite = sprites + (((y / 21 << 3) + column / 3) << 6) + y % 21 * 3 + column % 3;

      if (((_At(data, sprite) >> bit) & 1) != 0)
        color = _At(data, SpriteColorsOffset + column / 3);
      else {
        // The colour map holds both of a cell's colours in one byte; the bitmap picks the nibble.
        var offset = (y & ~7) * 3 + column;
        color = _At(data, ColorMapOffset + offset)
                >> (((_At(data, bitmap + (offset << 3) + (y & 7)) >> bit) & 1) << 2);
      }

      var entry = (color & 15) * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Character columns across the picture.</summary>
  public const int Columns = Width / 8;

  /// <summary>Character rows down the picture.</summary>
  public const int CellRows = Height / 8;

  /// <summary>
  /// Encodes a picture as two hires fields over one colour map, using the mixture of a cell's two
  /// colours as a third.
  /// </summary>
  /// <remarks>
  /// The two frames share the colour map, so what a cell can show is not two colours but three:
  /// either of them, or the two averaged where the frames disagree about a pixel. Encoding both
  /// frames alike would throw the third away and make the file twice the size of a picture it
  /// already held.
  /// <para/>
  /// No sprites are written. A sprite covers three character columns at one colour for the whole
  /// band, which is a worse thing to put over the picture than the picture, and the eight sprite
  /// colours only matter where a sprite pixel is lit.
  /// </remarks>
  public static ExtendSuperHiresFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var palette = Commodore64Graphics.CreatePalette();
    var data = new byte[UnpackedFileSize];

    Span<int> choice = stackalloc int[64];

    for (var cellRow = 0; cellRow < CellRows; ++cellRow)
    for (var column = 0; column < Columns; ++column) {
      var offset = cellRow * Columns + column;

      var bestHigh = 0;
      var bestLow = 0;
      var bestCost = long.MaxValue;

      for (var high = 0; high < Commodore64Graphics.ColorCount; ++high)
      for (var low = high; low < Commodore64Graphics.ColorCount; ++low) {
        long cost = 0;
        for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 8; ++x) {
          var at = ((cellRow * 8 + y) * Width + column * 8 + x) * 3;
          cost += _Cheapest(palette, high, low, rgb[at], rgb[at + 1], rgb[at + 2], out _);
        }

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestHigh = high;
        bestLow = low;
      }

      data[ColorMapOffset + offset] = (byte)((bestHigh << 4) | bestLow);

      for (var y = 0; y < 8; ++y) {
        int first = 0, second = 0;
        for (var x = 0; x < 8; ++x) {
          var at = ((cellRow * 8 + y) * Width + column * 8 + x) * 3;
          _Cheapest(palette, bestHigh, bestLow, rgb[at], rgb[at + 1], rgb[at + 2], out var shown);

          // The high nibble is what a set bit shows, so a pixel that wants the mixture sets the bit
          // in one frame and not the other.
          if (shown != 1)
            first |= 1 << (~x & 7);

          if (shown == 0)
            second |= 1 << (~x & 7);
        }

        data[FirstBitmapOffset + (offset << 3) + y] = (byte)first;
        data[SecondBitmapOffset + (offset << 3) + y] = (byte)second;
      }
    }

    return new() { Data = data };
  }

  /// <summary>
  /// The cost of the best of the three things a cell can show, and which of them it is: 0 the high
  /// nibble's colour, 1 the low nibble's, 2 the two averaged.
  /// </summary>
  private static int _Cheapest(
    ReadOnlySpan<byte> palette, int high, int low, byte red, byte green, byte blue, out int shown) {
    Span<byte> mixed = stackalloc byte[3];
    for (var channel = 0; channel < 3; ++channel) {
      int a = palette[high * 3 + channel], b = palette[low * 3 + channel];
      mixed[channel] = (byte)((a & b) + (((a ^ b) >> 1) & 0x7F));
    }

    var costs = (
      High: _Distance(palette[high * 3], palette[high * 3 + 1], palette[high * 3 + 2], red, green, blue),
      Low: _Distance(palette[low * 3], palette[low * 3 + 1], palette[low * 3 + 2], red, green, blue),
      Mixed: _Distance(mixed[0], mixed[1], mixed[2], red, green, blue));

    shown = 0;
    var best = costs.High;
    if (costs.Low < best) {
      best = costs.Low;
      shown = 1;
    }

    if (costs.Mixed >= best)
      return best;

    shown = 2;

    return costs.Mixed;
  }

  private static int _Distance(int r, int g, int b, byte red, byte green, byte blue) {
    int dr = r - red, dg = g - green, db = b - blue;

    return dr * dr + dg * dg + db * db;
  }
}
