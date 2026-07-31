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
  : IImageFormatReader<CharPadFile>, IImageToRawImage<CharPadFile> {

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
}
