using System;
using FileFormat.Core;

namespace FileFormat.SeuckSprites;

/// <summary>In-memory representation of a SEUCK sprite set (.a).</summary>
/// <remarks>
/// The 127 sprites a Shoot 'Em Up Construction Kit game could hold, shown as a contact sheet
/// sixteen across with two pixels of space around each. They are ordinary C64 multicolour sprites —
/// 24 pixels wide at two bits each, 21 rows, and a 64-byte record whose last byte is the sprite's
/// own colour and whose remaining three are unused.
/// <para/>
/// Two of the four colours are shared by every sprite on screen because the hardware has only one
/// pair of multicolour registers, so a sprite chooses just one of its three. That is why the sheet
/// shows the same black and white throughout and varies only in the third.
/// </remarks>
public readonly record struct SeuckSpritesFile
  : IImageFormatReader<SeuckSpritesFile>, IImageToRawImage<SeuckSpritesFile> {

  /// <summary>Sprites the set holds.</summary>
  public const int SpriteCount = 127;

  /// <summary>Bytes a sprite record occupies.</summary>
  public const int SpriteLength = 64;

  /// <summary>Screen pixels a sprite is wide.</summary>
  public const int SpriteWidth = 24;

  /// <summary>Rows a sprite is deep.</summary>
  public const int SpriteHeight = 21;

  /// <summary>Screen pixels a cell of the sheet occupies, sprite and gap together.</summary>
  public const int CellWidth = 26;

  /// <summary>Rows a cell of the sheet occupies.</summary>
  public const int CellHeight = 23;

  /// <summary>Sprites across.</summary>
  public const int Columns = 16;

  /// <summary>Pixels across.</summary>
  public const int Width = Columns * CellWidth;

  /// <summary>Rows.</summary>
  public const int Height = 8 * CellHeight - 2;

  /// <summary>Offset of the first sprite.</summary>
  public const int SpritesOffset = 2;

  /// <summary>Total file size.</summary>
  public const int FileSize = SpritesOffset + 127 * SpriteLength;

  /// <summary>The colour the space around the sprites takes.</summary>
  public const int BackgroundColor = 11;

  static string IImageFormatMetadata<SeuckSpritesFile>.PrimaryExtension => ".a";
  static string[] IImageFormatMetadata<SeuckSpritesFile>.FileExtensions => [".a"];
  static SeuckSpritesFile IImageFormatReader<SeuckSpritesFile>.FromSpan(ReadOnlySpan<byte> data)
    => SeuckSpritesReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SeuckSpritesFile>.VideoModes => [
    new("Sprite set", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SeuckSpritesFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var color = BackgroundColor;
      int row = y % CellHeight, column = x % CellWidth;

      if (row < SpriteHeight && column < SpriteWidth) {
        var sprite = x / CellWidth + ((y / CellHeight) << 4);
        if (sprite < SpriteCount) {
          var offset = SpritesOffset + sprite * SpriteLength;
          var at = offset + row * 3 + (column >> 3);
          var pattern = at < data.Length ? (data[at] >> (~column & 6)) & 3 : 0;

          // Two of the three colours are the same for every sprite; only the third is its own.
          color = pattern switch {
            1 => 0,
            2 => data[offset + SpriteLength - 1] & 15,
            3 => 1,
            _ => BackgroundColor,
          };
        }
      }

      pixels[y * Width + x] = (byte)color;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }
}
