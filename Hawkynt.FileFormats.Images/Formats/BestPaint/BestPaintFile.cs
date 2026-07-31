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
  : IImageFormatReader<BestPaintFile>, IImageToRawImage<BestPaintFile> {

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
}
