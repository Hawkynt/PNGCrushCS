using System;
using FileFormat.Core;

namespace FileFormat.SuperHiresEditor;

/// <summary>In-memory representation of a Super-hires Editor II picture (.sh2) for the Commodore 64.</summary>
/// <remarks>
/// A high-resolution bitmap with one row of hardware sprites laid over it. The bitmap gives two
/// colours per character cell; wherever a sprite covers a cell it contributes a third, taken from
/// its own colour register rather than the cell's — which is how the picture gets past the limit
/// the VIC-II otherwise imposes.
/// <para/>
/// Files come packed or not. The packed ones store their sprites column by column and the plain
/// ones the way the hardware wants them, which is the only difference between the two readings
/// beyond where everything sits.
/// </remarks>
public readonly record struct SuperHiresEditor2File
  : IImageFormatReader<SuperHiresEditor2File>, IImageToRawImage<SuperHiresEditor2File> {

  /// <summary>Displayed width.</summary>
  public const int Width = 192;

  /// <summary>Displayed height.</summary>
  public const int Height = 168;

  /// <summary>Size of a file that is not packed.</summary>
  public const int PlainFileSize = 14770;

  /// <summary>Size a packed file unpacks to.</summary>
  public const int UnpackedSize = 8576;

  static string IImageFormatMetadata<SuperHiresEditor2File>.PrimaryExtension => ".sh2";
  static string[] IImageFormatMetadata<SuperHiresEditor2File>.FileExtensions => [".sh2"];
  static SuperHiresEditor2File IImageFormatReader<SuperHiresEditor2File>.FromSpan(ReadOnlySpan<byte> data)
    => SuperHiresEditor2Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SuperHiresEditor2File>.VideoModes => [
    new("Super-hires II", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture's bytes, packed or not, as the reader settled them.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset of the bitmap.</summary>
  public int BitmapOffset { get; init; }

  /// <summary>Offset of the video matrix.</summary>
  public int VideoMatrixOffset { get; init; }

  /// <summary>Character cells between one video matrix row and the next.</summary>
  public int ScreenStride { get; init; }

  /// <summary>Offset of the sprite shapes.</summary>
  public int SpritesOffset { get; init; }

  /// <summary>Offset of the sprite colours, one per sprite.</summary>
  public int SpriteColorsOffset { get; init; }

  /// <summary>Whether the sprites are stored column by column rather than as the hardware wants.</summary>
  public bool ColumnSprites { get; init; }

  public static RawImage ToRawImage(SuperHiresEditor2File file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var bit = ~x & 7;
      var sprite = file.ColumnSprites
        ? SuperHiresLayout.ColumnSpriteOffset(x, y, Height)
        : SuperHiresLayout.SpriteOffset(x, y, 3);

      int color;
      if (((_At(data, file.SpritesOffset + sprite) >> bit) & 1) != 0)
        color = _At(data, file.SpriteColorsOffset + x / SuperHiresLayout.SpriteWidth);
      else {
        // Outside the sprites, the cell's two colours sit in one video matrix byte and the bitmap
        // bit picks which nibble to read.
        var cell = (y >> 3) * file.ScreenStride + (x >> 3);
        var set = (_At(data, file.BitmapOffset + (cell << 3) + (y & 7)) >> bit) & 1;
        color = _At(data, file.VideoMatrixOffset + cell) >> (set << 2);
      }

      pixels[y * Width + x] = (byte)(color & 15);
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

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
