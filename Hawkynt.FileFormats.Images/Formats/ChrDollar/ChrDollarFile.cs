using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ChrDollar;

/// <summary>In-memory representation of a CHR$ character set (.ch$).</summary>
/// <remarks>
/// A ZX Spectrum font stored with its colours: every cell carries an attribute byte of its own on
/// top of the eight bitmap bytes, which a font in ROM format cannot. Cells may also be stored twice,
/// in which case the two copies are alternating fields and the picture is their average — the
/// machine has no way to show more than two colours in a cell, so a font that wants more flickers
/// between two versions of itself.
/// </remarks>
public readonly record struct ChrDollarFile
  : IImageFormatReader<ChrDollarFile>, IImageToRawImage<ChrDollarFile>,
    IImageFromRawImage<ChrDollarFile>, IImageFormatWriter<ChrDollarFile> {

  /// <summary>Bytes a single-field cell occupies: eight rows of bitmap and one attribute.</summary>
  public const int BytesPerCell = 9;

  /// <summary>Bytes before the first cell: the signature, the two dimensions and the cell size.</summary>
  public const int HeaderSize = 7;

  static string IImageFormatMetadata<ChrDollarFile>.PrimaryExtension => ".ch$";
  static string[] IImageFormatMetadata<ChrDollarFile>.FileExtensions => [".ch$"];
  static ChrDollarFile IImageFormatReader<ChrDollarFile>.FromSpan(ReadOnlySpan<byte> data)
    => ChrDollarReader.FromSpan(data);
  static byte[] IImageFormatWriter<ChrDollarFile>.ToBytes(ChrDollarFile file) => ChrDollarWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ChrDollarFile>.VideoModes => [
    new("ZX Spectrum", [(IntegerRange.Any, IntegerRange.Any)], [15])
  ];

  /// <summary>Character cells across.</summary>
  public int Columns { get; init; }

  /// <summary>Character cells down.</summary>
  public int Rows { get; init; }

  /// <summary>Fields per cell: one, or two that alternate.</summary>
  public int Frames { get; init; }

  /// <summary>The cells, in row order and with a cell's fields consecutive.</summary>
  public byte[] Cells { get; init; }

  public static RawImage ToRawImage(ChrDollarFile file) {
    var width = file.Columns * 8;
    var height = file.Rows * 8;
    var cells = file.Cells ?? [];
    var fields = new byte[file.Frames][];
    for (var i = 0; i < fields.Length; ++i)
      fields[i] = new byte[width * height * 3];

    var at = 0;
    for (var row = 0; row < file.Rows; ++row)
    for (var column = 0; column < file.Columns; ++column)
    for (var field = 0; field < file.Frames; ++field, at += BytesPerCell) {
      if (at + BytesPerCell > cells.Length)
        throw new InvalidDataException("A CHR$ font ends before its last cell does.");

      var target = fields[field];
      var attribute = cells[at + 8];
      for (var y = 0; y < 8; ++y) {
        var bits = cells[at + y];
        for (var x = 0; x < 8; ++x)
          ZxSpectrumGraphics.WriteRgb(
            target, ((row * 8 + y) * width + column * 8 + x) * 3, attribute, ((bits >> (7 - x)) & 1) != 0);
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = fields.Length == 1 ? fields[0] : FrameBlend.Average(fields[0], fields[1]),
    };
  }

  /// <summary>Builds a font from a picture, a character cell at a time.</summary>
  /// <remarks>
  /// One field rather than two. Two fields exist to show colours a cell cannot hold by flickering
  /// between them, which needs a decision about what to trade for what — and a picture written as
  /// one field is a picture, where a badly chosen pair of fields is a flicker.
  /// <para/>
  /// The size is rounded up to whole cells, since a cell is the smallest thing the format has.
  /// </remarks>
  public static ChrDollarFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A font needs at least one pixel.", nameof(image));

    var columns = (image.Width + 7) / 8;
    var rows = (image.Height + 7) / 8;
    if (columns > 255 || rows > 255)
      throw new ArgumentException($"A font is at most 255 cells each way, got {columns}x{rows}.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);

    // The picture is padded out to whole cells so a cell at the edge has something to match.
    var width = columns * 8;
    var padded = new byte[width * rows * 8 * 3];
    for (var y = 0; y < image.Height; ++y)
      rgb.PixelData.AsSpan(y * image.Width * 3, image.Width * 3).CopyTo(padded.AsSpan(y * width * 3));

    var cells = new byte[rows * columns * BytesPerCell];
    var at = 0;
    var bits = new byte[8];

    for (var row = 0; row < rows; ++row)
    for (var column = 0; column < columns; ++column, at += BytesPerCell) {
      var attribute = ZxSpectrumGraphics.ChooseCell(padded, width, column * 8, row * 8, bits);
      bits.CopyTo(cells, at);
      cells[at + 8] = attribute;
    }

    return new() { Columns = columns, Rows = rows, Frames = 1, Cells = cells };
  }
}
