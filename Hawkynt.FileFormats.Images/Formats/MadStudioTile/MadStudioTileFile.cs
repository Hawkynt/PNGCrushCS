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
  : IImageFormatReader<MadStudioTileFile>, IImageToRawImage<MadStudioTileFile> {

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
}
