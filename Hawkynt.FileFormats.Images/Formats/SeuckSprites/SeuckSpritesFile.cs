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
  : IImageFormatReader<SeuckSpritesFile>, IImageToRawImage<SeuckSpritesFile>,
    IImageFromRawImage<SeuckSpritesFile>, IImageFormatWriter<SeuckSpritesFile> {

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
  static byte[] IImageFormatWriter<SeuckSpritesFile>.ToBytes(SeuckSpritesFile file)
    => SeuckSpritesWriter.ToBytes(file);
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

  /// <summary>
  /// Cuts a picture back into the 127 sprites the sheet shows, sampling it to the sheet's own size.
  /// </summary>
  /// <remarks>
  /// Only one of a sprite's four colours is its own; the other three are the same throughout because
  /// the hardware has one pair of multicolour registers and one background behind them. So the whole
  /// of the decision per sprite is that one colour, and all sixteen are tried against the 252 pairs
  /// of pixels it covers — the space around the sprites is not encoded at all, since nothing in the
  /// file describes it.
  /// </remarks>
  public static SeuckSpritesFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).EnsureFormat(PixelFormat.Rgb24).PixelData;
    var data = new byte[FileSize];

    // What the reader checks for; the second byte is the high half of a count the sheet does not use.
    data[0] = 66;

    // A multicolour pixel is two screen pixels wide, so a sprite is twelve pairs across.
    const int pairs = SpriteWidth / 2;
    var indices = new int[pairs * SpriteHeight];

    for (var sprite = 0; sprite < SpriteCount; ++sprite) {
      var left = (sprite & (Columns - 1)) * CellWidth;
      var top = sprite / Columns * CellHeight;

      for (var row = 0; row < SpriteHeight; ++row)
      for (var pair = 0; pair < pairs; ++pair) {
        var at = ((top + row) * Width + left + pair * 2) * 3;
        var next = at + 3;

        // The pair shows one colour, so both of its source pixels have a say in which.
        indices[row * pairs + pair] = Commodore64Graphics.FindNearestColorIndex(
          (byte)((rgb[at] + rgb[next]) / 2),
          (byte)((rgb[at + 1] + rgb[next + 1]) / 2),
          (byte)((rgb[at + 2] + rgb[next + 2]) / 2));
      }

      var own = _ChooseSpriteColor(indices);
      var offset = SpritesOffset + sprite * SpriteLength;
      data[offset + SpriteLength - 1] = (byte)own;

      for (var row = 0; row < SpriteHeight; ++row)
      for (var pair = 0; pair < pairs; ++pair) {
        var column = pair * 2;
        var pattern = _ChoosePattern(indices[row * pairs + pair], own);
        data[offset + row * 3 + (column >> 3)] |= (byte)(pattern << (~column & 6));
      }
    }

    return new() { Data = data };
  }

  /// <summary>The one colour of a sprite's four that the sprite itself gets to choose.</summary>
  private static int _ChooseSpriteColor(ReadOnlySpan<int> indices) {
    var best = 0;
    var bestError = long.MaxValue;

    for (var candidate = 0; candidate < Commodore64Graphics.ColorCount; ++candidate) {
      long error = 0;
      foreach (var index in indices)
        error += _Distance(index, _ChoosePattern(index, candidate) switch {
          1 => 0,
          2 => candidate,
          3 => 1,
          _ => BackgroundColor,
        });

      if (error >= bestError)
        continue;

      bestError = error;
      best = candidate;
    }

    return best;
  }

  /// <summary>Which of the four patterns comes closest to a wanted colour.</summary>
  private static int _ChoosePattern(int index, int own) {
    var best = 0;
    var bestDistance = _Distance(index, BackgroundColor);

    ReadOnlySpan<int> candidates = [0, own, 1];
    for (var pattern = 1; pattern <= 3; ++pattern) {
      var distance = _Distance(index, candidates[pattern - 1]);
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = pattern;
    }

    return best;
  }

  /// <summary>Squared distance in RGB between two of the machine's colours.</summary>
  private static int _Distance(int left, int right) {
    if (left == right)
      return 0;

    int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
    int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    int dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    int db = (a & 0xFF) - (b & 0xFF);

    return dr * dr + dg * dg + db * db;
  }
}
