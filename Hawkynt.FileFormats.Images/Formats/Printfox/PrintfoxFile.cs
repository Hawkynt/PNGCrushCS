using System;
using FileFormat.Core;

namespace FileFormat.Printfox;

/// <summary>In-memory representation of a Printfox picture (.gb).</summary>
/// <remarks>
/// A Commodore 64 desktop-publishing program's own format, and shaped by what it was for: pictures
/// are black on white and stored cell by cell rather than row by row, because what the program did
/// with them was send them to a printer a character at a time.
/// <para/>
/// One letter at the front says which of three it is — a screen, a double-sized screen, or a block
/// of arbitrary size that carries its own dimensions and a name. The third also counts its runs
/// differently from the other two, so the letter has to be consulted while unpacking and not only
/// before it.
/// </remarks>
public readonly record struct PrintfoxFile
  : IImageFormatReader<PrintfoxFile>, IImageToRawImage<PrintfoxFile>,
    IImageFromRawImage<PrintfoxFile>, IImageFormatWriter<PrintfoxFile> {

  /// <summary>Pixels a character cell covers, each way.</summary>
  public const int CellSize = 8;

  static string IImageFormatMetadata<PrintfoxFile>.PrimaryExtension => ".gb";
  static string[] IImageFormatMetadata<PrintfoxFile>.FileExtensions => [".gb"];
  static PrintfoxFile IImageFormatReader<PrintfoxFile>.FromSpan(ReadOnlySpan<byte> data)
    => PrintfoxReader.FromSpan(data);
  static byte[] IImageFormatWriter<PrintfoxFile>.ToBytes(PrintfoxFile file) => PrintfoxWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PrintfoxFile>.VideoModes => [
    new("Commodore 64", [(new(8, 2040), new(8, 2040))], [2])
  ];

  /// <summary>Character cells across.</summary>
  public int Columns { get; init; }

  /// <summary>Character cell rows.</summary>
  public int Rows { get; init; }

  /// <summary>The unpacked bitmap, one cell's eight bytes at a time.</summary>
  public byte[] Cells { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width => this.Columns * CellSize;

  /// <summary>Rows.</summary>
  public int Height => this.Rows * CellSize;

  public static RawImage ToRawImage(PrintfoxFile file) {
    var cells = file.Cells ?? [];
    var width = file.Width;
    var pixels = new byte[width * file.Height];

    for (var row = 0; row < file.Rows; ++row)
    for (var column = 0; column < file.Columns; ++column)
    for (var y = 0; y < CellSize; ++y) {
      var value = cells[(row * file.Columns + column) * CellSize + y];
      var target = (row * CellSize + y) * width + column * CellSize;

      for (var x = 0; x < CellSize; ++x)
        pixels[target + x] = (byte)((value >> (7 - x)) & 1);
    }

    return new() {
      Width = width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = 2,
    };
  }

  /// <summary>Builds a picture from an image, taking anything darker than mid grey as ink.</summary>
  /// <remarks>
  /// The picture is stored cell by cell, so the width and height are rounded up to whole cells and
  /// whatever the image does not reach stays white — the same thing the program itself would have
  /// left there.
  /// </remarks>
  public static PrintfoxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var columns = (image.Width + CellSize - 1) / CellSize;
    var rows = (image.Height + CellSize - 1) / CellSize;
    if (columns > 255 || rows > 255)
      throw new ArgumentException($"A block is at most 255 cells each way, got {columns}x{rows}.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var cells = new byte[rows * columns * CellSize];

    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < image.Width; ++x) {
      var source = (y * image.Width + x) * 3;
      var luminance = rgb.PixelData[source] * 77 + rgb.PixelData[source + 1] * 150 + rgb.PixelData[source + 2] * 29;
      if (luminance >= 128 * 256)
        continue;

      var cell = (y / CellSize * columns + x / CellSize) * CellSize + y % CellSize;
      cells[cell] |= (byte)(1 << (7 - x % CellSize));
    }

    return new() { Columns = columns, Rows = rows, Cells = cells };
  }
}
