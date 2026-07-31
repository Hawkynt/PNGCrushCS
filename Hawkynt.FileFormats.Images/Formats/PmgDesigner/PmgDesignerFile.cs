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
  : IImageFormatReader<PmgDesignerFile>, IImageToRawImage<PmgDesignerFile> {

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
}
