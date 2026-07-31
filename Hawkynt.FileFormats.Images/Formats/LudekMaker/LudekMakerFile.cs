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
  : IImageFormatReader<LudekMakerFile>, IImageToRawImage<LudekMakerFile> {

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
}
