using System;
using FileFormat.Core;

namespace FileFormat.BestPaint;

/// <summary>In-memory representation of a Best Paint picture (.bp).</summary>
/// <remarks>
/// A VIC-20 screen of 160x192 in the machine's high-resolution character mode: one bit a pixel
/// against a background shared by the whole screen and one ink colour per character cell. The cells
/// are twelve rows of sixteen scanlines rather than the usual eight, which is what the VIC-I can be
/// told to do and what gets 192 rows out of 240 bytes of screen memory.
/// <para/>
/// The bitmap is stored column by column — a whole column of twelve cells before the next — because
/// that is the order the character set occupies memory when a program defines one cell per screen
/// position.
/// </remarks>
public readonly record struct BestPaintFile
  : IImageFormatReader<BestPaintFile>, IImageToRawImage<BestPaintFile>,
    IImageFromRawImage<BestPaintFile>, IImageFormatWriter<BestPaintFile> {

  static byte[] IImageFormatWriter<BestPaintFile>.ToBytes(BestPaintFile file) => BestPaintWriter.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public const int Width = 160;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Cells across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Scanlines a cell spans.</summary>
  public const int CellHeight = 16;

  /// <summary>Cell rows.</summary>
  public const int Rows = Height / CellHeight;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Offset of the per-cell ink colours.</summary>
  public const int ColorsOffset = 3842;

  /// <summary>Offset of the byte holding the screen's shared background colour.</summary>
  public const int BackgroundOffset = 4082;

  /// <summary>Total file size.</summary>
  public const int FileSize = 4083;

  static string IImageFormatMetadata<BestPaintFile>.PrimaryExtension => ".bp";
  static string[] IImageFormatMetadata<BestPaintFile>.FileExtensions => [".bp"];
  static BestPaintFile IImageFormatReader<BestPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => BestPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BestPaintFile>.VideoModes => [
    new("Best Paint", [(Width, Height)], [Vic20Graphics.ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(BestPaintFile file) {
    var data = file.Data ?? [];
    var background = data[BackgroundOffset] >> 4;
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // Columns first: a cell's sixteen rows are consecutive, and a column's twelve cells follow.
      var at = BitmapOffset + ((((x >> 3) * Rows + (y >> 4)) << 4) + (y & 15));
      var set = at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0;

      pixels[y * Width + x] = set
        ? (byte)(data[ColorsOffset + (y >> 4) * 20 + (x >> 3)] & 15)
        : (byte)background;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Vic20Graphics.CreatePalette(),
      PaletteCount = Vic20Graphics.ColorCount,
    };
  }

  /// <summary>Builds a picture: one ink per eight-by-sixteen cell over a background all of them share.</summary>
  /// <remarks>
  /// The background may be any of the sixteen colours, but an ink may not — the chip can only draw
  /// the lower half of the palette in the foreground, and a file naming a higher one is not this
  /// format at all. So the two are chosen from different sets rather than from one.
  /// </remarks>
  public static BestPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var vic = Vic20Graphics.CreatePalette();
    var background = _ChooseBackground(rgb, vic);

    var data = new byte[FileSize];
    data[0] = 0;
    data[1] = 17;
    data[BackgroundOffset] = (byte)(background << 4);

    for (var row = 0; row < Rows; ++row)
    for (var column = 0; column < Columns; ++column) {
      var x0 = column * 8;
      var y0 = row * CellHeight;
      var ink = _ChooseInk(rgb, vic, background, x0, y0);
      data[ColorsOffset + row * 20 + column] = ink;

      for (var y = y0; y < y0 + CellHeight; ++y)
      for (var x = x0; x < x0 + 8; ++x) {
        var at = (y * Width + x) * 3;
        if (_Distance(rgb, at, vic, ink) >= _Distance(rgb, at, vic, background))
          continue;

        // Columns first: a cell's sixteen rows are consecutive, and a column's twelve cells follow.
        var target = BitmapOffset + (((column * Rows + row) << 4) + (y & 15));
        data[target] |= (byte)(1 << (~x & 7));
      }
    }

    return new() { Data = data };
  }

  /// <summary>The colour every cell falls back on, chosen from the whole palette.</summary>
  private static byte _ChooseBackground(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic) {
    byte best = 0;
    var bestCost = long.MaxValue;

    for (byte candidate = 0; candidate < Vic20Graphics.ColorCount; ++candidate) {
      long cost = 0;
      for (var at = 0; at + 2 < rgb.Length; at += 3)
        cost += _Distance(rgb, at, vic, candidate);

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  /// <summary>The colour a cell draws in, which only the lower half of the palette offers.</summary>
  private static byte _ChooseInk(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic, byte background, int x0, int y0) {
    byte best = 0;
    var bestCost = long.MaxValue;

    for (byte candidate = 0; candidate < Vic20Graphics.ForegroundColorCount; ++candidate) {
      long cost = 0;

      for (var y = y0; y < y0 + CellHeight; ++y)
      for (var x = x0; x < x0 + 8; ++x) {
        var at = (y * Width + x) * 3;
        cost += Math.Min(_Distance(rgb, at, vic, candidate), _Distance(rgb, at, vic, background));
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  private static long _Distance(ReadOnlySpan<byte> rgb, int at, ReadOnlySpan<byte> vic, int color) {
    var entry = color * 3;
    long dr = rgb[at] - vic[entry], dg = rgb[at + 1] - vic[entry + 1], db = rgb[at + 2] - vic[entry + 2];

    return dr * dr + dg * dg + db * db;
  }
}
