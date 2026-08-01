using System;
using FileFormat.Core;

namespace FileFormat.MadStudioTile;

/// <summary>In-memory representation of a Mad Studio ANTIC 4 tile set (.tl4).</summary>
/// <remarks>
/// A handful of 8x8 tiles laid out as a grid, four wide and five tall at most. Each tile is nine
/// bytes: eight rows of four two-bit pixels, and then a flag choosing which of two colours its
/// pattern 3 draws in — the same choice ANTIC mode 4 makes from a character code's high bit, stored
/// separately here because a tile has no character code to carry it.
/// <para/>
/// The four colours are Mad Studio's own; the file stores none.
/// </remarks>
public readonly record struct MadStudioTileFile
  : IImageFormatReader<MadStudioTileFile>, IImageToRawImage<MadStudioTileFile>,
    IImageFromRawImage<MadStudioTileFile>, IImageFormatWriter<MadStudioTileFile> {

  /// <summary>Screen pixels a tile spans; each of its four logical pixels is drawn two wide.</summary>
  public const int TileSize = 8;

  /// <summary>Bytes a tile occupies: eight rows and the colour flag.</summary>
  public const int TileLength = 9;

  /// <summary>Most tiles across.</summary>
  public const int MaxColumns = 4;

  /// <summary>Most tiles down.</summary>
  public const int MaxRows = 5;

  /// <summary>Offset of the first tile, after the grid size.</summary>
  public const int TileOffset = 2;

  /// <summary>The colours a pixel value draws in, before the flag chooses between the last two.</summary>
  public const byte Color1 = 40;
  public const byte Color2 = 202;
  public const byte Color3 = 148;
  public const byte Color3Alternate = 70;

  static string IImageFormatMetadata<MadStudioTileFile>.PrimaryExtension => ".tl4";
  static string[] IImageFormatMetadata<MadStudioTileFile>.FileExtensions => [".tl4"];
  static MadStudioTileFile IImageFormatReader<MadStudioTileFile>.FromSpan(ReadOnlySpan<byte> data)
    => MadStudioTileReader.FromSpan(data);
  static byte[] IImageFormatWriter<MadStudioTileFile>.ToBytes(MadStudioTileFile file)
    => MadStudioTileWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MadStudioTileFile>.VideoModes => [
    new("Tile set", [(new IntegerRange(TileSize, MaxColumns * TileSize), new IntegerRange(TileSize, MaxRows * TileSize))], [5])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Tiles across.</summary>
  public int Columns { get; init; }

  /// <summary>Tiles down.</summary>
  public int Rows { get; init; }

  public static RawImage ToRawImage(MadStudioTileFile file) {
    var data = file.Data ?? [];
    var width = file.Columns * TileSize;
    var height = file.Rows * TileSize;
    var frame = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var tile = TileOffset + ((y / TileSize) * file.Columns + x / TileSize) * TileLength;
      var row = tile + (y & 7);
      var pattern = row < data.Length ? (data[row] >> (~x & 6)) & 3 : 0;

      frame[y * width + x] = pattern switch {
        1 => Color1,
        2 => Color2,
        3 => tile + 8 < data.Length && data[tile + 8] != 0 ? Color3Alternate : Color3,
        _ => 0,
      };
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Builds a tile set, at the largest grid the format allows.</summary>
  /// <remarks>
  /// A tile shows four colours: the background, two fixed ones, and a fourth that its own flag byte
  /// chooses between two candidates. The flag is per tile rather than per pixel, so it is settled
  /// first — both candidates are tried across the whole tile and the cheaper kept — and only then
  /// is each logical pixel assigned.
  /// <para/>
  /// A logical pixel is two screen pixels wide, so the picture is read at the left one of each pair
  /// rather than averaged: averaging across a boundary the hardware cannot show would soften an
  /// edge that is going to be drawn hard anyway.
  /// </remarks>
  public static MadStudioTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int columns = MaxColumns, rows = MaxRows;
    var width = columns * TileSize;
    var height = rows * TileSize;
    var rgb = image.SampleTo(width, height);

    var data = new byte[TileOffset + columns * rows * TileLength];
    data[0] = columns;
    data[1] = rows;

    Span<byte> candidates = stackalloc byte[4];
    candidates[0] = 0;
    candidates[1] = Color1;
    candidates[2] = Color2;

    for (var tileY = 0; tileY < rows; ++tileY)
    for (var tileX = 0; tileX < columns; ++tileX) {
      var tile = TileOffset + (tileY * columns + tileX) * TileLength;

      var bestFlag = 0;
      var bestCost = long.MaxValue;
      Span<byte> bestRows = stackalloc byte[TileSize];

      for (var flag = 0; flag < 2; ++flag) {
        candidates[3] = flag != 0 ? Color3Alternate : Color3;

        var cost = 0L;
        Span<byte> pattern = stackalloc byte[TileSize];

        for (var y = 0; y < TileSize; ++y) {
          var value = 0;
          for (var pixel = 0; pixel < 4; ++pixel) {
            var at = ((tileY * TileSize + y) * width + tileX * TileSize + pixel * 2) * 3;
            var (choice, error) = _Nearest(rgb.PixelData, at, candidates);

            value |= choice << (6 - pixel * 2);
            cost += error;
          }

          pattern[y] = (byte)value;
        }

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestFlag = flag;
        pattern.CopyTo(bestRows);
      }

      for (var y = 0; y < TileSize; ++y)
        data[tile + y] = bestRows[y];

      data[tile + 8] = (byte)bestFlag;
    }

    return new() { Data = data, Columns = columns, Rows = rows };
  }

  /// <summary>Which of a tile's four colours a pixel is closest to, and by how much.</summary>
  private static (int Choice, long Error) _Nearest(
    ReadOnlySpan<byte> rgb, int pixel, ReadOnlySpan<byte> candidates) {
    var palette = Atari8BitGraphics.Palette;
    var best = 0;
    var bestCost = long.MaxValue;

    for (var i = 0; i < 4; ++i) {
      var entry = candidates[i] * 3;
      long dr = rgb[pixel] - palette[entry];
      long dg = rgb[pixel + 1] - palette[entry + 1];
      long db = rgb[pixel + 2] - palette[entry + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return (best, bestCost);
  }
}
