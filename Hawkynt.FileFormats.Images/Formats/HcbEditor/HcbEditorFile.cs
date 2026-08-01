using System;
using FileFormat.Core;

namespace FileFormat.HcbEditor;

/// <summary>In-memory representation of an HCB-editor picture (.hcb) for the Commodore 64.</summary>
/// <remarks>
/// A multicolour screen that changes two things the hardware normally fixes for the whole display.
/// The background colour is rewritten every four scanlines, and the video matrix alternates between
/// two copies on the same four-line cycle — so a character cell, which is eight lines tall, draws
/// its top half from one set of colours and its bottom half from the other.
/// <para/>
/// That doubles the colours available per cell at the cost of a raster interrupt every four lines,
/// which is where the name comes from. The picture is 296 pixels wide rather than 320 because the
/// interrupt costs the leftmost characters.
/// </remarks>
public readonly record struct HcbEditorFile
  : IImageFormatReader<HcbEditorFile>, IImageToRawImage<HcbEditorFile>,
    IImageFromRawImage<HcbEditorFile>, IImageFormatWriter<HcbEditorFile> {

  /// <summary>Displayed width; the raster interrupt costs the leftmost cells.</summary>
  public const int Width = 296;

  /// <summary>Displayed height.</summary>
  public const int Height = 200;

  /// <summary>Character cells a stored row spans, before the cropping.</summary>
  public const int StrideColumns = 40;

  /// <summary>Offset of the first video matrix.</summary>
  public const int VideoMatrixOffset = 2053;

  /// <summary>Distance to the second video matrix, which alternate four-line bands use.</summary>
  public const int VideoMatrixStride = 1024;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 4122;

  /// <summary>Offset of the background colours, one per four scanlines.</summary>
  public const int BackgroundOffset = 12098;

  /// <summary>Scanlines that share one background colour.</summary>
  public const int BackgroundBand = 4;

  /// <summary>Total file size.</summary>
  public const int FileSize = 12148;

  static string IImageFormatMetadata<HcbEditorFile>.PrimaryExtension => ".hcb";
  static string[] IImageFormatMetadata<HcbEditorFile>.FileExtensions => [".hcb"];
  static HcbEditorFile IImageFormatReader<HcbEditorFile>.FromSpan(ReadOnlySpan<byte> data) => HcbEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<HcbEditorFile>.ToBytes(HcbEditorFile file) => HcbEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HcbEditorFile>.VideoModes => [
    new("HCB", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(HcbEditorFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      // Both the colour source and the background follow the same four-line cycle.
      var matrix = VideoMatrixOffset + ((y & BackgroundBand) << 8);
      var background = (byte)(_At(data, BackgroundOffset + y / BackgroundBand) & 15);

      for (var x = 0; x < Width; ++x) {
        var cell = (y >> 3) * StrideColumns + (x >> 3);
        var pattern = (_At(data, BitmapOffset + (cell << 3) + (y & 7)) >> (~x & 6)) & 3;

        pixels[y * Width + x] = (byte)(pattern switch {
          1 => _At(data, matrix + cell) >> 4,
          2 => _At(data, matrix + cell) & 15,
          3 => _At(data, matrix + cell) & 15,
          _ => background,
        });
      }
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

  private static byte _At(ReadOnlySpan<byte> data, int offset) => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a picture, with two colour sources alternating every four scanlines.</summary>
  /// <remarks>
  /// The trick this format is named for: a raster interrupt swaps the video matrix and the
  /// background every four lines, so a cell's top half and bottom half each get their own pair of
  /// colours. That doubles what a cell can show, and it is why the picture is 296 wide rather than
  /// 320 — the interrupt costs the leftmost characters.
  /// <para/>
  /// The background is shared by every cell in a band, so it is settled first, per band, before any
  /// cell chooses its own two. A cell's halves are then independent: each row belongs to exactly
  /// one of them, so nothing has to be traded between the two.
  /// </remarks>
  public static HcbEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var data = new byte[FileSize];
    var columns = Width / 8;

    var backgrounds = new byte[Height / BackgroundBand];
    for (var band = 0; band < backgrounds.Length; ++band) {
      backgrounds[band] = _CommonestIn(rgb.PixelData, band * BackgroundBand);
      data[BackgroundOffset + band] = backgrounds[band];
    }

    for (var cellRow = 0; cellRow < Height / 8; ++cellRow)
    for (var column = 0; column < columns; ++column) {
      var cell = cellRow * StrideColumns + column;

      for (var half = 0; half < 2; ++half) {
        var top = cellRow * 8 + half * BackgroundBand;
        var background = backgrounds[top / BackgroundBand];
        var (high, low, rows) = _ChooseHalf(rgb.PixelData, column * 8, top, background);

        data[VideoMatrixOffset + half * VideoMatrixStride + cell] = (byte)((high << 4) | low);
        for (var y = 0; y < BackgroundBand; ++y)
          data[BitmapOffset + (cell << 3) + half * BackgroundBand + y] = rows[y];
      }
    }

    return new() { Data = data };
  }

  /// <summary>The two colours that describe one half of a cell best, beside its band's background.</summary>
  private static (int High, int Low, byte[] Rows) _ChooseHalf(
    ReadOnlySpan<byte> rgb, int left, int top, byte background) {
    int bestHigh = 0, bestLow = 0;
    var bestCost = long.MaxValue;
    var best = new byte[BackgroundBand];
    var rows = new byte[BackgroundBand];

    for (var high = 0; high < Commodore64Graphics.ColorCount; ++high)
    for (var low = 0; low < Commodore64Graphics.ColorCount; ++low) {
      var cost = 0L;

      for (var y = 0; y < BackgroundBand; ++y) {
        var value = 0;
        for (var pixel = 0; pixel < 4; ++pixel) {
          var at = ((top + y) * Width + left + pixel * 2) * 3;
          var toBackground = _Distance(rgb, at, background);
          var toHigh = _Distance(rgb, at, high);
          var toLow = _Distance(rgb, at, low);

          var pattern = 0;
          var least = toBackground;
          if (toHigh < least) { pattern = 1; least = toHigh; }
          if (toLow < least) { pattern = 2; least = toLow; }

          value |= pattern << (6 - pixel * 2);
          cost += least;
        }

        rows[y] = (byte)value;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      bestHigh = high;
      bestLow = low;
      Array.Copy(rows, best, rows.Length);
    }

    return (bestHigh, bestLow, best);
  }

  /// <summary>The machine's colour that appears most across one band of scanlines.</summary>
  private static byte _CommonestIn(ReadOnlySpan<byte> rgb, int top) {
    Span<int> totals = stackalloc int[Commodore64Graphics.ColorCount];
    for (var y = top; y < top + BackgroundBand; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = (y * Width + x) * 3;
      ++totals[Commodore64Graphics.FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2])];
    }

    var best = 0;
    for (var i = 1; i < totals.Length; ++i)
      if (totals[i] > totals[best])
        best = i;

    return (byte)best;
  }

  private static long _Distance(ReadOnlySpan<byte> rgb, int pixel, int entry) {
    var color = Commodore64Graphics.HexColors[entry];
    long dr = rgb[pixel] - ((color >> 16) & 0xFF);
    long dg = rgb[pixel + 1] - ((color >> 8) & 0xFF);
    long db = rgb[pixel + 2] - (color & 0xFF);

    return dr * dr * 77 + dg * dg * 150 + db * db * 29;
  }
}
