using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.IPaint;

/// <summary>In-memory representation of an I Paint picture (.ip).</summary>
/// <remarks>
/// A Commodore 128 picture in the 80-column display's own terms: one bit a pixel, with colour — if
/// there is any — held separately and at a coarser grain. The colour is stored two rows to every
/// eight of the picture, and the two alternate down the block, so a cell shows one pair of colours
/// on its even rows and another on its odd ones. That is not a compression trick but what the
/// display chip could actually be made to do.
/// <para/>
/// The colour is optional: a file may simply end after its bitmap, in which case the picture is
/// black on white.
/// </remarks>
public readonly record struct IPaintFile
  : IImageFormatReader<IPaintFile>, IImageToRawImage<IPaintFile>,
    IImageFromRawImage<IPaintFile>, IImageFormatWriter<IPaintFile> {

  /// <summary>Colours the 80-column chip can show.</summary>
  public const int ColorCount = 16;

  /// <summary>Character cells the header's single byte can count.</summary>
  public const int MaximumColumns = 90;

  /// <summary>Rows the program allowed, which is less than the field could hold.</summary>
  public const int MaximumHeight = 700;

  /// <summary>The sixteen colours the 80-column chip can show.</summary>
  /// <remarks>
  /// Not the VIC-II's sixteen. The 80-column chip is a different design with the same palette a PC
  /// of the time had: two levels of each channel, a pair of greys — and, in place of the dark
  /// yellow an even ramp would give, brown. That one entry is the giveaway, and reading it as
  /// 0xAAAA00 rather than 0xAA5500 is the mistake the table invites.
  /// </remarks>
  public static ReadOnlySpan<int> Palette => [
    0x000000, 0x555555, 0x0000AA, 0x5555FF, 0x00AA00, 0x55FF55, 0x00AAAA, 0x55FFFF,
    0xAA0000, 0xFF5555, 0xAA00AA, 0xFF55FF, 0xAA5500, 0xFFFF55, 0xAAAAAA, 0xFFFFFF,
  ];

  static string IImageFormatMetadata<IPaintFile>.PrimaryExtension => ".ip";
  static string[] IImageFormatMetadata<IPaintFile>.FileExtensions => [".ip"];
  static IPaintFile IImageFormatReader<IPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => IPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<IPaintFile>.ToBytes(IPaintFile file) => IPaintWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<IPaintFile>.VideoModes => [
    new("Commodore 128", [(new(8, 720), new(1, 700))], [2, 16])
  ];

  /// <summary>Character cells across.</summary>
  public int Columns { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The unpacked bitmap, one bit a pixel.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>
  /// The unpacked colours, two rows of cells for every eight rows of picture, or empty if the file
  /// carried none.
  /// </summary>
  public byte[] Colors { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width => this.Columns * 8;

  public static RawImage ToRawImage(IPaintFile file) {
    var bitmap = file.Bitmap ?? [];
    var colors = file.Colors ?? [];
    var width = file.Width;
    var rgb = new byte[width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < width; ++x) {
      var column = x >> 3;
      var lit = ((bitmap[y * file.Columns + column] >> (~x & 7)) & 1) != 0;

      int color;
      if (colors.Length == 0)
        color = lit ? 0 : 0xFFFFFF;
      else {
        // Two stored rows serve eight picture rows, taken alternately.
        var attribute = colors[(y >> 3) * file.Columns * 2 + (y & 1) * file.Columns + column];
        color = Palette[(lit ? attribute : attribute >> 4) & 15];
      }

      var target = (y * width + x) * 3;
      rgb[target] = (byte)(color >> 16);
      rgb[target + 1] = (byte)(color >> 8);
      rgb[target + 2] = (byte)color;
    }

    return new() { Width = width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Fits a picture to the 80-column display: one bit a pixel, with two of the chip's sixteen
  /// colours for every cell and every second row within it.
  /// </summary>
  /// <remarks>
  /// The colour is stored two rows to every eight of the picture and the two alternate down the
  /// block, so a cell's even rows and its odd rows are coloured independently. Each of those halves
  /// is given the pair of colours that costs it least, tried exhaustively — there are only sixteen
  /// colours, so all 256 pairs are cheaper to measure than to be clever about.
  /// <para/>
  /// The width is a whole number of cells and nothing else, so a picture that is not is sampled to
  /// the nearest that is rather than refused.
  /// </remarks>
  public static IPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var columns = Math.Clamp((image.Width + 7) >> 3, 1, MaximumColumns);
    var height = Math.Clamp(image.Height, 1, MaximumHeight);
    var width = columns * 8;
    var source = image.SampleTo(width, height);

    var bitmap = new byte[height * columns];
    var blocks = (height + 7) >> 3;
    var colors = new byte[blocks * columns * 2];

    var places = new List<int>(32);
    var costs = new long[32 * ColorCount];

    for (var block = 0; block < blocks; ++block)
    for (var column = 0; column < columns; ++column)
    for (var parity = 0; parity < 2; ++parity) {
      places.Clear();
      for (var y = block * 8 + parity; y < Math.Min(block * 8 + 8, height); y += 2)
      for (var x = column * 8; x < Math.Min(column * 8 + 8, width); ++x)
        places.Add(y * width + x);

      for (var i = 0; i < places.Count; ++i)
      for (var color = 0; color < ColorCount; ++color)
        costs[i * ColorCount + color] = _Cost(source.PixelData, places[i], color);

      var (paper, ink) = _ChoosePair(costs, places.Count);
      colors[block * columns * 2 + parity * columns + column] = (byte)((paper << 4) | ink);

      for (var i = 0; i < places.Count; ++i) {
        if (costs[i * ColorCount + ink] > costs[i * ColorCount + paper])
          continue;

        var x = places[i] % width;
        bitmap[places[i] / width * columns + column] |= (byte)(1 << (~x & 7));
      }
    }

    return new() { Columns = columns, Height = height, Bitmap = bitmap, Colors = colors };
  }

  /// <summary>The pair of colours costing a cell's half of the rows least, over all 256 of them.</summary>
  private static (int Paper, int Ink) _ChoosePair(ReadOnlySpan<long> costs, int count) {
    var best = (0, 0);
    var bestCost = long.MaxValue;

    for (var paper = 0; paper < ColorCount; ++paper)
    for (var ink = 0; ink < ColorCount; ++ink) {
      long cost = 0;
      for (var i = 0; i < count; ++i)
        cost += Math.Min(costs[i * ColorCount + paper], costs[i * ColorCount + ink]);

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (paper, ink);
    }

    return best;
  }

  private static long _Cost(ReadOnlySpan<byte> rgb, int pixel, int index) {
    var at = pixel * 3;
    var color = Palette[index];
    long dr = rgb[at] - (byte)(color >> 16), dg = rgb[at + 1] - (byte)(color >> 8), db = rgb[at + 2] - (byte)color;

    return dr * dr + dg * dg + db * db;
  }
}
