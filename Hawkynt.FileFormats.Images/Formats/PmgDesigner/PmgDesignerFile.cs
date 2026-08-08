using System;
using FileFormat.Core;

namespace FileFormat.PmgDesigner;

/// <summary>In-memory representation of a PMG Designer sheet (.pmd).</summary>
/// <remarks>
/// Every shape of every sprite in one file, arranged as a contact sheet sixteen across. Sprites are
/// stored and drawn in pairs — the GTIA ORs the colours of players sharing a pixel, so a pair shows
/// three colours where one shows one — which halves the number of cells the sheet needs and is why
/// the shape count in the header is twice what appears.
/// <para/>
/// A sheet of a single row is laid out to its own width; anything taller is 320 pixels across with
/// two blank scanlines between rows.
/// </remarks>
public readonly record struct PmgDesignerFile
  : IImageFormatReader<PmgDesignerFile>, IImageToRawImage<PmgDesignerFile>,
    IImageFromRawImage<PmgDesignerFile>, IImageFormatWriter<PmgDesignerFile> {

  /// <summary>Shapes a sprite may have, which the header states as two factors.</summary>
  public const int MaximumShapes = 160;

  /// <summary>Scanlines a shape may span.</summary>
  public const int WrittenHeight = 24;

  /// <summary>Screen pixels a pair of players covers, of the twenty a cell is given.</summary>
  public const int PairWidth = 16;

  /// <summary>The three bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [240, 237, 228];

  /// <summary>Offset of the shapes.</summary>
  public const int ShapesOffset = 11;

  /// <summary>Offset of the colours, two per sprite pair.</summary>
  public const int ColorsOffset = 3;

  /// <summary>Screen pixels a cell occupies.</summary>
  public const int CellWidth = 20;

  /// <summary>Cells a full-width row holds.</summary>
  public const int CellsPerRow = 16;

  /// <summary>Blank scanlines between rows of cells.</summary>
  public const int RowGap = 2;

  /// <summary>Width of a sheet more than one row deep.</summary>
  public const int FullWidth = 320;

  static string IImageFormatMetadata<PmgDesignerFile>.PrimaryExtension => ".pmd";
  static string[] IImageFormatMetadata<PmgDesignerFile>.FileExtensions => [".pmd"];
  static PmgDesignerFile IImageFormatReader<PmgDesignerFile>.FromSpan(ReadOnlySpan<byte> data)
    => PmgDesignerReader.FromSpan(data);
  static byte[] IImageFormatWriter<PmgDesignerFile>.ToBytes(PmgDesignerFile file) => PmgDesignerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PmgDesignerFile>.VideoModes => [
    new("Sprite sheet", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Shapes each sprite has.</summary>
  public int Shapes { get; init; }

  /// <summary>Cells the sheet shows: one per pair of sprites per shape.</summary>
  public int Cells { get; init; }

  /// <summary>Scanlines a shape spans.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(PmgDesignerFile file) {
    var data = file.Data ?? [];
    var rows = (file.Cells + CellsPerRow - 1) / CellsPerRow;
    var width = rows == 1 ? file.Cells * CellWidth : FullWidth;
    var height = rows == 1 ? file.Height : rows * (file.Height + RowGap) - RowGap;
    var frame = new byte[width * height];

    for (var cell = 0; cell < file.Cells; ++cell) {
      var frameOffset = (cell / CellsPerRow) * (file.Height + RowGap) * width + (cell % CellsPerRow) * CellWidth;

      // Each cell is one pair, whose two halves sit one after the other in the shape list.
      var pair = cell / file.Shapes;
      var offset = ShapesOffset + (pair * file.Shapes + cell) * file.Height;

      Atari8BitGraphics.DrawPlayerInto(
        data, offset, data[ColorsOffset + pair * 2], frame, frameOffset, width, file.Height, true);
      Atari8BitGraphics.DrawPlayerInto(
        data, offset + file.Shapes * file.Height, data[ColorsOffset + 1 + pair * 2], frame, frameOffset, width,
        file.Height, true);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Fills a contact sheet of sprite shapes with a picture, sixteen shapes across.</summary>
  /// <remarks>
  /// A sheet is not a picture of a chosen size: it is a grid of shapes, and its size follows from
  /// how many there are. The count is therefore chosen to make the grid nearest the shape of the
  /// picture, and the picture is sampled to what that grid comes to.
  /// <para/>
  /// Two sprites are written, which is one pair: the chip ORs the colours of players sharing a
  /// pixel, so a pair shows three colours where one shows one, and one pair is what a sheet with a
  /// single row of colours can say. What the pair does not cover keeps the background: the last four
  /// pixels of every twenty-pixel cell, and the two scanlines between rows.
  /// </remarks>
  public static PmgDesignerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rows = Math.Clamp(
      (int)Math.Round((double)image.Height * FullWidth / ((double)image.Width * (WrittenHeight + RowGap))),
      1, MaximumShapes / CellsPerRow);

    var cells = rows * CellsPerRow;
    var height = rows * (WrittenHeight + RowGap) - RowGap;
    var source = image.SampleTo(FullWidth, height);

    var (first, second) = _ChoosePair(source.PixelData);
    var data = new byte[ShapesOffset + 2 * cells * WrittenHeight];

    Signature.CopyTo(data);
    data[ColorsOffset] = first;
    data[ColorsOffset + 1] = second;
    data[7] = 2;
    data[8] = (byte)cells;
    data[9] = 1;
    data[10] = WrittenHeight;

    var gtia = Atari8BitGraphics.Palette;
    for (var cell = 0; cell < cells; ++cell) {
      var left = cell % CellsPerRow * CellWidth;
      var top = cell / CellsPerRow * (WrittenHeight + RowGap);
      var offset = ShapesOffset + cell * WrittenHeight;

      for (var y = 0; y < WrittenHeight; ++y)
      for (var bit = 0; bit < 8; ++bit) {
        var value = _ChooseValue(source.PixelData, gtia, left + bit * 2, top + y, first, second);

        if ((value & 1) != 0)
          data[offset + y] |= (byte)(1 << (7 - bit));

        if ((value & 2) != 0)
          data[offset + cells * WrittenHeight + y] |= (byte)(1 << (7 - bit));
      }
    }

    return new() { Data = data, Shapes = cells, Cells = cells, Height = WrittenHeight };
  }

  /// <summary>
  /// Which of the pair's two players to light for a pixel: neither, one, the other, or both for the
  /// colour the chip makes by ORing them.
  /// </summary>
  private static int _ChooseValue(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> gtia, int x, int y, byte first, byte second) {
    var at = (y * FullWidth + x) * 3;
    var best = 0;
    var bestCost = long.MaxValue;

    for (var value = 0; value < 4; ++value) {
      var color = ((value & 1) != 0 ? first & 254 : 0) | ((value & 2) != 0 ? second & 254 : 0);
      var entry = color * 3;
      long dr = rgb[at] - gtia[entry], dg = rgb[at + 1] - gtia[entry + 1], db = rgb[at + 2] - gtia[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = value;
    }

    return best;
  }

  /// <summary>
  /// The pair of registers whose four combinations cost the picture least, measured over the colours
  /// it holds rather than over its pixels.
  /// </summary>
  /// <remarks>
  /// Both registers ignore their low bit, so there are 128 of each and 8256 unordered pairs. Against
  /// a picture that would be billions of comparisons; against a histogram of the colours in it, a
  /// few million.
  /// </remarks>
  private static (byte First, byte Second) _ChoosePair(ReadOnlySpan<byte> rgb) {
    var counts = new int[4096];
    for (var at = 0; at + 2 < rgb.Length; at += 3)
      ++counts[((rgb[at] >> 4) << 8) | ((rgb[at + 1] >> 4) << 4) | (rgb[at + 2] >> 4)];

    var gtia = Atari8BitGraphics.Palette;
    var best = ((byte)0, (byte)0);
    var bestCost = long.MaxValue;

    for (var first = 0; first < 256; first += 2)
    for (var second = first; second < 256; second += 2) {
      long cost = 0;

      for (var bin = 0; bin < counts.Length && cost < bestCost; ++bin) {
        if (counts[bin] == 0)
          continue;

        int red = (bin >> 8) * 17, green = ((bin >> 4) & 15) * 17, blue = (bin & 15) * 17;
        var nearest = long.MaxValue;

        for (var value = 0; value < 4; ++value) {
          var entry = (((value & 1) != 0 ? first : 0) | ((value & 2) != 0 ? second : 0)) * 3;
          long dr = red - gtia[entry], dg = green - gtia[entry + 1], db = blue - gtia[entry + 2];
          nearest = Math.Min(nearest, dr * dr + dg * dg + db * db);
        }

        cost += nearest * counts[bin];
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = ((byte)first, (byte)second);
    }

    return best;
  }
}
