using System;

namespace FileFormat.Core;

/// <summary>
/// The geometry and the memory layout the Commodore 64 FLI pictures share.
/// </summary>
/// <remarks>
/// FLI is one trick: the video matrix is pointed somewhere new on every raster line of a character
/// cell, so each of the eight lines chooses its own colours instead of the cell choosing once for
/// all eight. Eight matrices is eight kilobytes and it buys eight times the colour resolution down
/// the screen.
/// <para/>
/// It costs the left of the screen. The switch happens while the raster is still in the border and
/// cannot be ready before the first three character cells are drawn, so the leftmost 24 pixels of
/// every row show whatever the hardware happened to be holding. They are not part of the picture:
/// what a FLI picture is, is the remaining 296 across.
/// <para/>
/// The matrices themselves sit a page apart rather than end to end. A video matrix is a thousand
/// entries but the VIC-II addresses one every 1024 bytes, so the twenty-four bytes after each are
/// address space the picture cannot use and the file carries them anyway. Writing the thousand
/// packed instead makes a file that round-trips through its own reader perfectly and that nothing
/// else on earth can open, which is the mistake this type exists to stop being made a ninth time.
/// </remarks>
public static class Commodore64Fli {

  /// <summary>Pixels a row of screen memory holds.</summary>
  public const int MemoryWidth = 320;

  /// <summary>Pixels at the left of a row the raster switch cannot reach.</summary>
  public const int HiddenColumns = 24;

  /// <summary>Pixels across a FLI picture: the row less the cells drawn before the switch.</summary>
  public const int VisibleWidth = MemoryWidth - HiddenColumns;

  /// <summary>Raster lines a whole screen holds.</summary>
  public const int ScreenHeight = 200;

  /// <summary>Video matrices a FLI picture carries, one for each raster line of a cell.</summary>
  public const int MatrixCount = 8;

  /// <summary>Bytes from one video matrix to the next: a whole page for the thousand it uses.</summary>
  public const int MatrixStride = 1024;

  /// <summary>Entries one video matrix holds.</summary>
  public const int MatrixEntries = Commodore64Graphics.Columns * (ScreenHeight / Commodore64Graphics.CellHeight);

  /// <summary>Bytes all eight matrices take together.</summary>
  public const int MatrixAreaSize = MatrixCount * MatrixStride;

  /// <summary>Bytes a whole-screen bitmap takes.</summary>
  public const int BitmapSize = Commodore64Graphics.Columns * (ScreenHeight / Commodore64Graphics.CellHeight) * Commodore64Graphics.CellHeight;

  /// <summary>Bytes colour memory takes.</summary>
  public const int ColorRamSize = MatrixEntries;

  /// <summary>Multicolour pixels a row of screen memory holds; each is drawn two wide.</summary>
  public const int MulticolorMemoryWidth = MemoryWidth / 2;

  /// <summary>Multicolour pixels at the left of a row the raster switch cannot reach.</summary>
  public const int MulticolorHiddenColumns = HiddenColumns / 2;

  /// <summary>Multicolour pixels across the picture.</summary>
  public const int MulticolorVisibleWidth = MulticolorMemoryWidth - MulticolorHiddenColumns;

  /// <summary>Decodes a multicolour FLI picture into the 296 pixels across that are the picture.</summary>
  /// <param name="bitmap">The bitmap, addressed from the start of screen memory.</param>
  /// <param name="matrices">The eight video matrices, <paramref name="matrixStride"/> apart.</param>
  /// <param name="matrixStride">Bytes from one matrix to the next.</param>
  /// <param name="colorRam">Colour memory, one entry a cell, which pattern 11 takes.</param>
  /// <param name="backgrounds">
  /// What pattern 00 shows: one entry for the whole picture, or one for each raster line where the
  /// format changes it down the screen.
  /// </param>
  /// <param name="height">Rows the picture has.</param>
  /// <param name="firstRow">The raster line of screen memory the picture's first row is.</param>
  public static RawImage DecodeMulticolor(
    ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> matrices, int matrixStride,
    ReadOnlySpan<byte> colorRam, ReadOnlySpan<byte> backgrounds, int height, int firstRow = 0) {
    var indices = new byte[VisibleWidth * height];

    for (var y = 0; y < height; ++y) {
      var row = y + firstRow;
      var line = row % Commodore64Graphics.CellHeight;
      var background = (backgrounds.Length <= 1 ? backgrounds.IsEmpty ? (byte)0 : backgrounds[0] : backgrounds[y]) & 0x0F;

      for (var x = 0; x < VisibleWidth; ++x) {
        var column = x + HiddenColumns;
        var cell = row / Commodore64Graphics.CellHeight * Commodore64Graphics.Columns + column / 8;
        var pattern = bitmap[cell * Commodore64Graphics.CellHeight + line] >> (~column & 6) & 3;
        var entry = matrices[line * matrixStride + cell];

        indices[y * VisibleWidth + x] = (byte)(pattern switch {
          0 => background,
          1 => entry >> 4,
          2 => entry & 0x0F,
          _ => colorRam[cell] & 0x0F,
        });
      }
    }

    return _Picture(indices, height);
  }

  /// <summary>Decodes a high-resolution FLI picture into the 296 pixels across that are the picture.</summary>
  public static RawImage DecodeHires(
    ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> matrices, int matrixStride, int height, int firstRow = 0) {
    var indices = new byte[VisibleWidth * height];

    for (var y = 0; y < height; ++y) {
      var row = y + firstRow;
      var line = row % Commodore64Graphics.CellHeight;

      for (var x = 0; x < VisibleWidth; ++x) {
        var column = x + HiddenColumns;
        var cell = row / Commodore64Graphics.CellHeight * Commodore64Graphics.Columns + column / 8;
        var lit = (bitmap[cell * Commodore64Graphics.CellHeight + line] >> (~column & 7) & 1) != 0;
        var entry = matrices[line * matrixStride + cell];

        indices[y * VisibleWidth + x] = (byte)(lit ? entry >> 4 : entry & 0x0F);
      }
    }

    return _Picture(indices, height);
  }

  /// <summary>Encodes a picture as a multicolour FLI screen, placed where the raster can reach it.</summary>
  /// <remarks>
  /// The picture handed in is the 296 across that a FLI picture is, and it goes into screen memory
  /// three cells from the left with black in front of it. Those cells are real bytes in a real file
  /// and nothing ever reads them back, because the switch that makes FLI work has not happened yet
  /// when they are drawn.
  /// </remarks>
  /// <returns>The rows of screen memory the picture occupies, rounded out to whole cells.</returns>
  public static int EncodeMulticolor(
    RawImage image, int height, int firstRow, byte background,
    Span<byte> bitmap, Span<byte> matrices, int matrixStride, Span<byte> colorRam) {
    ArgumentNullException.ThrowIfNull(image);

    var rows = _WholeCells(firstRow + height);
    var rgb = _Place(image, MulticolorMemoryWidth, MulticolorHiddenColumns, height, firstRow, rows);
    Commodore64Graphics.EncodeMulticolorFli(
      rgb, MulticolorMemoryWidth, rows, background, bitmap, matrices, matrixStride, colorRam);

    return rows;
  }

  /// <summary>Encodes a picture as a high-resolution FLI screen, placed where the raster can reach it.</summary>
  public static int EncodeHires(
    RawImage image, int height, int firstRow, Span<byte> bitmap, Span<byte> matrices, int matrixStride) {
    ArgumentNullException.ThrowIfNull(image);

    var rows = _WholeCells(firstRow + height);
    var rgb = _Place(image, MemoryWidth, HiddenColumns, height, firstRow, rows);
    Commodore64Graphics.EncodeHiresFli(rgb, MemoryWidth, rows, bitmap, matrices, matrixStride);

    return rows;
  }

  private static int _WholeCells(int rows)
    => (rows + Commodore64Graphics.CellHeight - 1) / Commodore64Graphics.CellHeight * Commodore64Graphics.CellHeight;

  /// <summary>Scales the picture to what the format holds and lays it into screen memory.</summary>
  private static byte[] _Place(RawImage image, int memoryWidth, int hidden, int height, int firstRow, int rows) {
    var visibleWidth = memoryWidth - hidden;
    var visible = image.SampleTo(visibleWidth, height).PixelData;
    var rgb = new byte[memoryWidth * rows * 3];

    for (var y = 0; y < height; ++y)
      visible.AsSpan(y * visibleWidth * 3, visibleWidth * 3)
        .CopyTo(rgb.AsSpan(((firstRow + y) * memoryWidth + hidden) * 3));

    return rgb;
  }

  private static RawImage _Picture(byte[] indices, int height) => new() {
    Width = VisibleWidth,
    Height = height,
    Format = PixelFormat.Indexed8,
    PixelData = indices,
    Palette = Commodore64Graphics.CreatePalette(),
    PaletteCount = Commodore64Graphics.ColorCount,
  };
}
