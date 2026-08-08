using System;
using FileFormat.Core;

namespace FileFormat.Fuckpaint;

/// <summary>In-memory representation of a Fuckpaint picture (.fp) for the Commodore 64.</summary>
/// <remarks>
/// Two multicolour screens shown on alternate television fields, with the second displaced one
/// pixel left of the first. The displacement is the point: two screens in register would average
/// into a duller version of themselves, whereas offsetting them lets each pixel pair with a
/// different neighbour and produces colours the VIC-II has no register for.
/// <para/>
/// The two screens share one colour RAM and one background, so what differs between the fields is
/// only the bitmap and the video matrix.
/// </remarks>
public readonly record struct FuckpaintFile
  : IImageFormatReader<FuckpaintFile>, IImageToRawImage<FuckpaintFile>,
    IImageFromRawImage<FuckpaintFile>, IImageFormatWriter<FuckpaintFile> {

  /// <summary>Displayed width.</summary>
  public const int Width = 320;

  /// <summary>Displayed height.</summary>
  public const int Height = 200;

  /// <summary>Character cells across a row.</summary>
  public const int Columns = Width / 8;

  /// <summary>Offset of the colour RAM, which both fields share.</summary>
  public const int ColorRamOffset = 2;

  /// <summary>Offset of the first field's video matrix.</summary>
  public const int FirstMatrixOffset = 1026;

  /// <summary>Offset of the second field's video matrix.</summary>
  public const int SecondMatrixOffset = 2050;

  /// <summary>Offset of the first field's bitmap.</summary>
  public const int FirstBitmapOffset = 3074;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondBitmapOffset = 11266;

  /// <summary>Offset of the background colour, which both fields share.</summary>
  public const int BackgroundOffset = 11074;

  /// <summary>How far left the second field sits.</summary>
  public const int SecondFieldShift = 1;

  /// <summary>Total file size.</summary>
  public const int FileSize = 19266;

  static string IImageFormatMetadata<FuckpaintFile>.PrimaryExtension => ".fp";
  static string[] IImageFormatMetadata<FuckpaintFile>.FileExtensions => [".fp"];
  static FuckpaintFile IImageFormatReader<FuckpaintFile>.FromSpan(ReadOnlySpan<byte> data) => FuckpaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<FuckpaintFile>.ToBytes(FuckpaintFile file) => FuckpaintWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FuckpaintFile>.VideoModes => [
    new("Fuckpaint", [(Width, Height)], [Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because every area is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(FuckpaintFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();
    var background = (byte)(_At(data, BackgroundOffset) & 15);

    var first = _RenderField(data, FirstBitmapOffset, FirstMatrixOffset, background, 0, palette);
    var second = _RenderField(data, SecondBitmapOffset, SecondMatrixOffset, background, SecondFieldShift, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  /// <summary>Draws one field, optionally displaced left.</summary>
  private static byte[] _RenderField(
    ReadOnlySpan<byte> data, int bitmap, int matrix, byte background, int shift, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var source = x - shift;

      // Displacing the field leaves its leftmost column with nothing to show but the background.
      var index = source < 0 ? background : _ColorAt(data, bitmap, matrix, background, source, y);
      var entry = index * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }

  /// <summary>The palette entry a multicolour pixel draws from.</summary>
  private static byte _ColorAt(ReadOnlySpan<byte> data, int bitmap, int matrix, byte background, int x, int y) {
    var cell = (y >> 3) * Columns + (x >> 3);
    var pattern = (_At(data, bitmap + (cell << 3) + (y & 7)) >> (~x & 6)) & 3;

    return (byte)(pattern switch {
      1 => _At(data, matrix + cell) >> 4,
      2 => _At(data, matrix + cell) & 15,
      3 => _At(data, ColorRamOffset + cell) & 15,
      _ => background,
    });
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Multicolour pixels across a field, each drawn two screen pixels wide.</summary>
  public const int LogicalWidth = Width / 2;

  /// <summary>
  /// Encodes a picture as two multicolour fields, the second reading the column to the right of what
  /// the first reads.
  /// </summary>
  /// <remarks>
  /// The displacement is the point of the format, so the two fields are not given the same picture:
  /// the first field's pixel says what the odd screen column holds and the second's says what the
  /// even column to its right holds. Averaging the pair then puts a colour between the two wherever
  /// they differ, which is what the VIC-II has no register for. Encoding both fields alike would
  /// give a duller version of one field and spend nine thousand bytes saying it twice.
  /// <para/>
  /// Displacing the second field leaves the leftmost screen column with nothing behind it, so what
  /// shows there is the average of the first field's pixel and the background — a fact about the
  /// format rather than about the picture, and the one column no choice of bytes can control.
  /// <para/>
  /// The colour memory and the background are shared, so they are settled from the first field and
  /// the second is fitted around them; only the video matrix is the second field's own.
  /// </remarks>
  public static FuckpaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var data = new byte[FileSize];

    var first = _Field(rgb, 1);
    var bitmap = new byte[8000];
    var matrix = new byte[1000];
    var colorRam = new byte[1000];
    var background = Commodore64Graphics.EncodeMulticolor(first, LogicalWidth, Height, bitmap, matrix, colorRam);

    bitmap.CopyTo(data, FirstBitmapOffset);
    matrix.CopyTo(data, FirstMatrixOffset);
    colorRam.CopyTo(data, ColorRamOffset);
    data[BackgroundOffset] = background;

    _FitSecondField(_Field(rgb, 2), background, colorRam, data);

    return new() { Data = data };
  }

  /// <summary>The colours one field's pixels stand for, a screen column at a time.</summary>
  private static byte[] _Field(ReadOnlySpan<byte> rgb, int offset) {
    var field = new byte[LogicalWidth * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var pixel = 0; pixel < LogicalWidth; ++pixel) {
      var x = Math.Min(pixel * 2 + offset, Width - 1);
      var from = (y * Width + x) * 3;
      var to = (y * LogicalWidth + pixel) * 3;
      rgb.Slice(from, 3).CopyTo(field.AsSpan(to));
    }

    return field;
  }

  /// <summary>
  /// Chooses the second field's video matrix and bitmap against a background and colour memory it
  /// does not own.
  /// </summary>
  private static void _FitSecondField(
    ReadOnlySpan<byte> field, byte background, ReadOnlySpan<byte> colorRam, Span<byte> data) {
    var distance = new int[Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount];
    for (var left = 0; left < Commodore64Graphics.ColorCount; ++left)
    for (var right = 0; right < Commodore64Graphics.ColorCount; ++right) {
      int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
      int dr = ((a >> 16) & 255) - ((b >> 16) & 255);
      int dg = ((a >> 8) & 255) - ((b >> 8) & 255);
      int db = (a & 255) - (b & 255);
      distance[left * Commodore64Graphics.ColorCount + right] = dr * dr + dg * dg + db * db;
    }

    Span<int> cell = stackalloc int[32];

    for (var cellRow = 0; cellRow < Height / 8; ++cellRow)
    for (var column = 0; column < Columns; ++column) {
      var index = cellRow * Columns + column;
      var third = colorRam[index] & 15;

      for (var y = 0; y < 8; ++y)
      for (var pixel = 0; pixel < 4; ++pixel) {
        var at = ((cellRow * 8 + y) * LogicalWidth + column * 4 + pixel) * 3;
        cell[y * 4 + pixel] = Commodore64Graphics.FindNearestColorIndex(field[at], field[at + 1], field[at + 2]);
      }

      int bestHigh = 0, bestLow = 0;
      var bestCost = long.MaxValue;

      for (var high = 0; high < Commodore64Graphics.ColorCount; ++high)
      for (var low = 0; low < Commodore64Graphics.ColorCount; ++low) {
        long cost = 0;
        foreach (var colour in cell) {
          var at = colour * Commodore64Graphics.ColorCount;
          var best = distance[at + background];
          best = Math.Min(best, distance[at + high]);
          best = Math.Min(best, distance[at + low]);
          cost += Math.Min(best, distance[at + third]);
        }

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestHigh = high;
        bestLow = low;
      }

      data[SecondMatrixOffset + index] = (byte)((bestHigh << 4) | bestLow);

      for (var y = 0; y < 8; ++y) {
        var bits = 0;
        for (var pixel = 0; pixel < 4; ++pixel) {
          var at = cell[y * 4 + pixel] * Commodore64Graphics.ColorCount;
          var pattern = 0;
          var best = distance[at + background];

          for (var candidate = 1; candidate < 4; ++candidate) {
            var cost = distance[at + (candidate == 1 ? bestHigh : candidate == 2 ? bestLow : third)];
            if (cost >= best)
              continue;

            best = cost;
            pattern = candidate;
          }

          bits |= pattern << ((3 - pixel) << 1);
        }

        data[SecondBitmapOffset + (index << 3) + y] = (byte)bits;
      }
    }
  }
}
