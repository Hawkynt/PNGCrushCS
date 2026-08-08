using System;
using FileFormat.Core;

namespace FileFormat.CharPad;

/// <summary>In-memory representation of a CharPad project (.ctm).</summary>
/// <remarks>
/// Not a picture but the pieces a C64 game's screen is built from: a character set, optionally
/// tiles made of characters, and a map naming tiles. Drawing it means walking all three, which is
/// exactly what the machine does — the levels of indirection exist because a game holds far more
/// screen than memory, and each level trades a lookup for the space it saves.
/// <para/>
/// Where a character's foreground colour comes from is the file's own choice, and it is the choice
/// that matters: per project, per character, or per tile. The last is only possible when there are
/// tiles at all, which is why that combination is rejected rather than defaulted.
/// </remarks>
public readonly record struct CharPadFile
  : IImageFormatReader<CharPadFile>, IImageToRawImage<CharPadFile>,
    IImageFromRawImage<CharPadFile>, IImageFormatWriter<CharPadFile> {

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "CTM";

  /// <summary>The version this reader understands.</summary>
  public const byte Version = 5;

  /// <summary>Offset of the character set.</summary>
  public const int CharactersOffset = 20;

  /// <summary>Bytes a character occupies in the set, including the byte after its eight rows.</summary>
  public const int CharacterLength = 9;

  /// <summary>Offset of the colour every character shares when the project names only one.</summary>
  public const int ProjectColorOffset = 7;

  /// <summary>Offset of the three colours the whole screen shares.</summary>
  public const int SharedColorsOffset = 4;

  static string IImageFormatMetadata<CharPadFile>.PrimaryExtension => ".ctm";
  static string[] IImageFormatMetadata<CharPadFile>.FileExtensions => [".ctm"];
  static CharPadFile IImageFormatReader<CharPadFile>.FromSpan(ReadOnlySpan<byte> data)
    => CharPadReader.FromSpan(data);
  static byte[] IImageFormatWriter<CharPadFile>.ToBytes(CharPadFile file) => CharPadWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CharPadFile>.VideoModes => [
    new("CharPad", [(IntegerRange.Any, IntegerRange.Any)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Where a character's foreground comes from: 0 the project, 1 the tile, 2 the character.</summary>
  public int ColorMethod { get; init; }

  /// <summary>Whether the map names tiles rather than characters directly.</summary>
  public bool HasTiles { get; init; }

  /// <summary>Whether a tile's characters follow from its number rather than from a table.</summary>
  public bool CharactersAreImplied { get; init; }

  /// <summary>Whether a pixel is two bits against four colours rather than one against two.</summary>
  public bool IsMulticolor { get; init; }

  /// <summary>Characters the set holds.</summary>
  public int CharacterCount { get; init; }

  /// <summary>Characters across one tile.</summary>
  public int TileWidth { get; init; }

  /// <summary>Characters down one tile.</summary>
  public int TileHeight { get; init; }

  /// <summary>Tiles across the map.</summary>
  public int MapWidth { get; init; }

  /// <summary>Offset of the tile table.</summary>
  public int TilesOffset { get; init; }

  /// <summary>Offset of the per-tile colours.</summary>
  public int TileColorsOffset { get; init; }

  /// <summary>Offset of the map.</summary>
  public int MapOffset { get; init; }

  public static RawImage ToRawImage(CharPadFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y) {
      var mapRow = file.MapOffset + ((y >> 3) / file.TileHeight * file.MapWidth << 1);

      for (var x = 0; x < file.Width; ++x) {
        var mapEntry = mapRow + ((x >> 3) / file.TileWidth << 1);
        var tile = data[mapEntry] | (data[mapEntry + 1] << 8);

        var character = tile;
        if (file.HasTiles) {
          // A tile's characters are laid out in reading order, and either follow from its number
          // or are named one by one in a table.
          character = (tile * file.TileHeight + (y >> 3) % file.TileHeight) * file.TileWidth
                      + (x >> 3) % file.TileWidth;

          if (!file.CharactersAreImplied) {
            var at = file.TilesOffset + (character << 1);
            character = data[at] | (data[at + 1] << 8);
          }
        }

        var foreground = file.ColorMethod switch {
          1 => file.TileColorsOffset + tile,
          2 => CharactersOffset + (file.CharacterCount << 3) + character,
          _ => ProjectColorOffset,
        };

        var bits = data[CharactersOffset + (character << 3) + (y & 7)];
        int color;

        if (file.IsMulticolor) {
          var pattern = (bits >> (~x & 6)) & 3;
          // Only pattern 3 uses the per-character or per-tile colour, and only three bits of it —
          // the fourth bit of a colour byte is what tells the chip the cell is multicoloured.
          color = pattern == 3 ? data[foreground] & 7 : data[SharedColorsOffset + pattern];
        } else
          color = data[((bits >> (~x & 7)) & 1) == 0 ? SharedColorsOffset : foreground];

        pixels[y * file.Width + x] = (byte)(color & 15);
      }
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  /// <summary>Cells a map entry can name, since a character code is two bytes.</summary>
  public const int MaxCharacters = 65536;

  /// <summary>
  /// Colours pattern 11 can take: the fourth bit of a colour byte is the multicolour flag rather
  /// than part of the colour.
  /// </summary>
  public const int CellColorCount = 8;

  /// <summary>
  /// Encodes a picture as a multicolour project with one character per cell and a colour per
  /// character.
  /// </summary>
  /// <remarks>
  /// Neither tiles nor a shared character set are used. Both exist because a game holds far more
  /// screen than memory and each level of indirection trades a lookup for the space it saves; a
  /// single picture has nothing to save, and giving every cell its own character is what lets the
  /// picture be whatever it is rather than whatever 256 characters can say.
  /// <para/>
  /// Multicolour rather than high resolution. High resolution would give a cell two colours at
  /// eight pixels across and multicolour gives it four at four across, and four colours is the
  /// better trade on this machine for anything that was not drawn as a two-colour logo.
  /// <para/>
  /// Three of the four colours are shared by the whole screen and only pattern 11 is the cell's own,
  /// and that one is read out of a colour byte whose fourth bit tells the chip the cell is
  /// multicoloured — so only the low eight of the machine's sixteen colours can go there.
  /// </remarks>
  public static CharPadFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var columns = Math.Max(1, (image.Width + 4) / 8);
    var rows = Math.Max(1, (image.Height + 4) / 8);
    while ((long)columns * rows > MaxCharacters) {
      columns = Math.Max(1, columns >> 1);
      rows = Math.Max(1, rows >> 1);
    }

    var width = columns * 8;
    var height = rows * 8;
    var rgb = image.SampleTo(width, height).PixelData;

    // A multicolour pixel is two screen pixels wide, so only every other column is looked at.
    var logicalWidth = width >> 1;
    var indices = new int[logicalWidth * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < logicalWidth; ++x) {
      var at = (y * width + x * 2) * 3;
      indices[y * logicalWidth + x] = Commodore64Graphics.FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2]);
    }

    var distance = _DistanceTable();
    var shared = _ChooseShared(indices, distance);
    var cells = columns * rows;

    var data = new byte[CharactersOffset + cells * CharacterLength + cells * 2];
    data[0] = (byte)'C';
    data[1] = (byte)'T';
    data[2] = (byte)'M';
    data[3] = Version;
    data[SharedColorsOffset] = (byte)shared[0];
    data[SharedColorsOffset + 1] = (byte)shared[1];
    data[SharedColorsOffset + 2] = (byte)shared[2];
    data[8] = 2;
    data[9] = 4;
    data[10] = (byte)(cells - 1);
    data[11] = (byte)((cells - 1) >> 8);
    data[16] = (byte)columns;
    data[17] = (byte)(columns >> 8);
    data[18] = (byte)rows;
    data[19] = (byte)(rows >> 8);

    var colorsOffset = CharactersOffset + (cells << 3);
    var mapOffset = CharactersOffset + cells * CharacterLength;

    for (var row = 0; row < rows; ++row)
    for (var column = 0; column < columns; ++column) {
      var cell = row * columns + column;
      data[mapOffset + (cell << 1)] = (byte)cell;
      data[mapOffset + (cell << 1) + 1] = (byte)(cell >> 8);

      // Which of the eight colours pattern 11 takes is the only choice a cell has of its own.
      var best = 0;
      var bestCost = long.MaxValue;
      for (var candidate = 0; candidate < CellColorCount; ++candidate) {
        long cost = 0;
        for (var y = 0; y < 8; ++y)
        for (var pixel = 0; pixel < 4; ++pixel)
          cost += _PixelCost(
            distance, indices[(row * 8 + y) * logicalWidth + column * 4 + pixel], shared, candidate);

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = candidate;
      }

      data[colorsOffset + cell] = (byte)best;

      for (var y = 0; y < 8; ++y) {
        var bits = 0;
        for (var pixel = 0; pixel < 4; ++pixel) {
          var index = indices[(row * 8 + y) * logicalWidth + column * 4 + pixel];
          bits |= _Pattern(distance, index, shared, best) << ((3 - pixel) << 1);
        }

        data[CharactersOffset + (cell << 3) + y] = (byte)bits;
      }
    }

    return new() {
      Data = data,
      Width = width,
      Height = height,
      ColorMethod = 2,
      HasTiles = false,
      CharactersAreImplied = false,
      IsMulticolor = true,
      CharacterCount = cells,
      TileWidth = 1,
      TileHeight = 1,
      MapWidth = columns,
      TilesOffset = mapOffset,
      TileColorsOffset = mapOffset,
      MapOffset = mapOffset,
    };
  }

  /// <summary>Squared distance between every pair of the machine's colours.</summary>
  private static int[] _DistanceTable() {
    var table = new int[Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount];
    for (var left = 0; left < Commodore64Graphics.ColorCount; ++left)
    for (var right = 0; right < Commodore64Graphics.ColorCount; ++right) {
      int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
      int dr = ((a >> 16) & 255) - ((b >> 16) & 255);
      int dg = ((a >> 8) & 255) - ((b >> 8) & 255);
      int db = (a & 255) - (b & 255);
      table[left * Commodore64Graphics.ColorCount + right] = dr * dr + dg * dg + db * db;
    }

    return table;
  }

  /// <summary>
  /// The three colours the whole screen shares, taken as the three that appear most often.
  /// </summary>
  /// <remarks>
  /// They are shared by every cell, so a colour that is rare overall costs the picture little
  /// wherever it is missed and a common one costs it everywhere — which is the opposite of the case
  /// within a cell, where a rare mark is the pixel anyone looking at the picture sees first.
  /// </remarks>
  private static int[] _ChooseShared(ReadOnlySpan<int> indices, ReadOnlySpan<int> distance) {
    var counts = new int[Commodore64Graphics.ColorCount];
    foreach (var index in indices)
      ++counts[index];

    var shared = new int[3];
    for (var slot = 0; slot < shared.Length; ++slot) {
      var best = 0;
      for (var colour = 1; colour < Commodore64Graphics.ColorCount; ++colour)
        if (counts[colour] > counts[best])
          best = colour;

      shared[slot] = best;
      counts[best] = -1;
    }

    return shared;
  }

  private static int _Pattern(ReadOnlySpan<int> distance, int index, ReadOnlySpan<int> shared, int cellColor) {
    var best = 0;
    var bestCost = distance[index * Commodore64Graphics.ColorCount + shared[0]];

    for (var pattern = 1; pattern < 4; ++pattern) {
      var colour = pattern < 3 ? shared[pattern] : cellColor;
      var cost = distance[index * Commodore64Graphics.ColorCount + colour];
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = pattern;
    }

    return best;
  }

  private static int _PixelCost(ReadOnlySpan<int> distance, int index, ReadOnlySpan<int> shared, int cellColor) {
    var best = distance[index * Commodore64Graphics.ColorCount + cellColor];
    foreach (var colour in shared)
      best = Math.Min(best, distance[index * Commodore64Graphics.ColorCount + colour]);

    return best;
  }
}
