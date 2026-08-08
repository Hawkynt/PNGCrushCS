using System;
using FileFormat.Core;

namespace FileFormat.LudekMaker;

/// <summary>In-memory representation of a Ludek Maker sheet (.ldm).</summary>
/// <remarks>
/// A contact sheet of figures, eight across. Each is four players rather than one: two overlapping
/// pairs, the second sitting eight pixels right of the first, so a figure is sixteen pixels wide
/// and shows three colours where a single player shows one. The two colours are fixed for the whole
/// sheet, which is what makes it a sheet of one character's poses rather than of unrelated sprites.
/// </remarks>
public readonly record struct LudekMakerFile
  : IImageFormatReader<LudekMakerFile>, IImageToRawImage<LudekMakerFile>,
    IImageFromRawImage<LudekMakerFile>, IImageFormatWriter<LudekMakerFile> {

  /// <summary>Figures a sheet may hold, which is what the reader accepts.</summary>
  public const int MaximumShapes = 100;

  /// <summary>Screen pixels a figure covers: two overlapping pairs, the second eight pixels along.</summary>
  public const int FigureWidth = 32;

  /// <summary>The text every file starts with, each byte written with its high bit set.</summary>
  public const string Signature = "Ludek Maker data file";

  /// <summary>Offset of the first colour.</summary>
  public const int FirstColorOffset = 21;

  /// <summary>Offset of the second colour.</summary>
  public const int SecondColorOffset = 22;

  /// <summary>Offset of the shapes.</summary>
  public const int ShapesOffset = 281;

  /// <summary>Scanlines a figure spans.</summary>
  public const int FigureHeight = 30;

  /// <summary>Bytes a figure occupies: four players of thirty scanlines.</summary>
  public const int FigureLength = FigureHeight * 4;

  /// <summary>Screen pixels a cell occupies.</summary>
  public const int CellWidth = 40;

  /// <summary>Cells a full-width row holds.</summary>
  public const int CellsPerRow = 8;

  /// <summary>Scanlines a row of cells occupies, including the gap below it.</summary>
  public const int RowHeight = 32;

  /// <summary>Width of a sheet more than one row deep.</summary>
  public const int FullWidth = 320;

  /// <summary>Pixels the second pair sits right of the first.</summary>
  public const int PairOffset = 16;

  static string IImageFormatMetadata<LudekMakerFile>.PrimaryExtension => ".ldm";
  static string[] IImageFormatMetadata<LudekMakerFile>.FileExtensions => [".ldm"];
  static LudekMakerFile IImageFormatReader<LudekMakerFile>.FromSpan(ReadOnlySpan<byte> data)
    => LudekMakerReader.FromSpan(data);
  static byte[] IImageFormatWriter<LudekMakerFile>.ToBytes(LudekMakerFile file) => LudekMakerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<LudekMakerFile>.VideoModes => [
    new("Figure sheet", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Figures the sheet holds.</summary>
  public int Shapes { get; init; }

  public static RawImage ToRawImage(LudekMakerFile file) {
    var data = file.Data ?? [];
    var rows = (file.Shapes + CellsPerRow - 1) / CellsPerRow;
    var width = rows == 1 ? file.Shapes * CellWidth : FullWidth;
    var height = rows == 1 ? FigureHeight : rows * RowHeight - 2;
    var frame = new byte[width * height];

    int first = data[FirstColorOffset], second = data[SecondColorOffset];

    for (var shape = 0; shape < file.Shapes; ++shape) {
      var offset = ShapesOffset + shape * FigureLength;
      var frameOffset = (shape / CellsPerRow) * RowHeight * width + (shape % CellsPerRow) * CellWidth;

      Atari8BitGraphics.DrawPlayerInto(data, offset, first, frame, frameOffset, width, FigureHeight, true);
      Atari8BitGraphics.DrawPlayerInto(data, offset + FigureHeight, second, frame, frameOffset, width, FigureHeight, true);
      Atari8BitGraphics.DrawPlayerInto(
        data, offset + FigureHeight * 2, first, frame, frameOffset + PairOffset, width, FigureHeight, true);
      Atari8BitGraphics.DrawPlayerInto(
        data, offset + FigureHeight * 3, second, frame, frameOffset + PairOffset, width, FigureHeight, true);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Fills a contact sheet of figures with a picture, eight figures across.</summary>
  /// <remarks>
  /// A sheet is not a picture of a chosen size: it is a grid of figures, and its size follows from
  /// how many there are. The number is therefore chosen to make the grid nearest the shape of the
  /// picture, and the picture is sampled to what that grid comes to.
  /// <para/>
  /// Every figure is drawn by two overlapping pairs of players in the same two colours, which the
  /// chip ORs where they meet — so four values are available and two of them are chosen, the sheet's
  /// pair being one choice for all of it. What the grid does not cover keeps the background: the
  /// last eight pixels of every forty-pixel cell, and the two scanlines between rows.
  /// </remarks>
  public static LudekMakerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rows = Math.Clamp(
      (int)Math.Round((double)image.Height * FullWidth / ((double)image.Width * RowHeight)), 1,
      MaximumShapes / CellsPerRow);

    var shapes = rows * CellsPerRow;
    var height = rows * RowHeight - 2;
    var source = image.SampleTo(FullWidth, height);

    var (first, second) = _ChoosePair(source.PixelData);
    var data = new byte[ShapesOffset + shapes * FigureLength];

    for (var i = 0; i < Signature.Length; ++i)
      data[i] = (byte)(Signature[i] + 128);

    data[FirstColorOffset] = first;
    data[SecondColorOffset] = second;
    data[24] = (byte)shapes;

    var gtia = Atari8BitGraphics.Palette;
    for (var shape = 0; shape < shapes; ++shape) {
      var left = shape % CellsPerRow * CellWidth;
      var top = shape / CellsPerRow * RowHeight;
      var offset = ShapesOffset + shape * FigureLength;

      for (var y = 0; y < FigureHeight; ++y)
      for (var pair = 0; pair < 2; ++pair)
      for (var bit = 0; bit < 8; ++bit) {
        var x = left + pair * PairOffset + bit * 2;
        var value = _ChooseValue(source.PixelData, gtia, x, top + y, first, second);

        if ((value & 1) != 0)
          data[offset + pair * FigureHeight * 2 + y] |= (byte)(1 << (7 - bit));

        if ((value & 2) != 0)
          data[offset + (pair * 2 + 1) * FigureHeight + y] |= (byte)(1 << (7 - bit));
      }
    }

    return new() { Data = data, Shapes = shapes };
  }

  /// <summary>
  /// Which of the two players to light for a pixel: neither, one, the other, or both for the colour
  /// the chip makes by ORing them.
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
  /// it actually holds rather than over its pixels.
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
