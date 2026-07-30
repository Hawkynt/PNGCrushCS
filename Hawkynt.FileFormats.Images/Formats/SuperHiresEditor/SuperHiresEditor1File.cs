using System;
using FileFormat.Core;

namespace FileFormat.SuperHiresEditor;

/// <summary>In-memory representation of a Super-hires Editor I picture (.sh1) for the Commodore 64.</summary>
/// <remarks>
/// The earlier of the two editors, and the more extravagant: it lays <em>two</em> layers of sprites
/// over the bitmap rather than one, so a cell can show four colours — its own pair plus a
/// foreground and a background sprite colour. The cost is width, since twice the sprites cover half
/// the screen, which is why the picture is 96 pixels across.
/// <para/>
/// A plain file gives the two layers separate colour tables. A packed one has them share a single
/// table, the foreground taking the high nibble and the background the low, which is a saving that
/// only works because the two can never want more than sixteen colours between them.
/// </remarks>
public readonly record struct SuperHiresEditor1File
  : IImageFormatReader<SuperHiresEditor1File>, IImageToRawImage<SuperHiresEditor1File> {

  /// <summary>Displayed width.</summary>
  public const int Width = 96;

  /// <summary>Displayed height.</summary>
  public const int Height = 168;

  /// <summary>Size of a file that is not packed.</summary>
  public const int PlainFileSize = 14770;

  /// <summary>Size a packed file unpacks to.</summary>
  public const int UnpackedSize = 6304;

  static string IImageFormatMetadata<SuperHiresEditor1File>.PrimaryExtension => ".sh1";
  static string[] IImageFormatMetadata<SuperHiresEditor1File>.FileExtensions => [".sh1"];
  static SuperHiresEditor1File IImageFormatReader<SuperHiresEditor1File>.FromSpan(ReadOnlySpan<byte> data)
    => SuperHiresEditor1Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SuperHiresEditor1File>.VideoModes => [
    new("Super-hires I", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture's bytes, packed or not, as the reader settled them.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset of the bitmap.</summary>
  public int BitmapOffset { get; init; }

  /// <summary>Offset of the video matrix.</summary>
  public int VideoMatrixOffset { get; init; }

  /// <summary>Character cells between one video matrix row and the next.</summary>
  public int ScreenStride { get; init; }

  /// <summary>Offset of the foreground sprite layer.</summary>
  public int ForegroundSpritesOffset { get; init; }

  /// <summary>Offset of the background sprite layer.</summary>
  public int BackgroundSpritesOffset { get; init; }

  /// <summary>Offset of the foreground sprite colours.</summary>
  public int ForegroundColorsOffset { get; init; }

  /// <summary>Offset of the background sprite colours; equal to the foreground when they share.</summary>
  public int BackgroundColorsOffset { get; init; }

  /// <summary>Sprites across the picture, as a shift; zero means they are stored column by column.</summary>
  public int RowShift { get; init; }

  public static RawImage ToRawImage(SuperHiresEditor1File file) {
    var data = file.Data ?? [];
    var shared = file.ForegroundColorsOffset == file.BackgroundColorsOffset;
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var bit = ~x & 7;
      var sprite = file.RowShift == 0
        ? SuperHiresLayout.ColumnSpriteOffset(x, y, Height)
        : SuperHiresLayout.SpriteOffset(x, y, file.RowShift);
      var band = x / SuperHiresLayout.SpriteWidth;

      int color;
      if (((_At(data, file.ForegroundSpritesOffset + sprite) >> bit) & 1) != 0)
        // Sharing one table means the foreground lives in the high nibble.
        color = _At(data, file.ForegroundColorsOffset + band) >> (shared ? 4 : 0);
      else if (((_At(data, file.BackgroundSpritesOffset + sprite) >> bit) & 1) != 0)
        color = _At(data, file.BackgroundColorsOffset + band);
      else {
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
