using System;
using FileFormat.Core;

namespace FileFormat.MonoMagic;

/// <summary>In-memory representation of a Mono Magic C64 image image.</summary>
public readonly record struct MonoMagicFile : IImageFormatReader<MonoMagicFile>, IImageToRawImage<MonoMagicFile>, IImageFromRawImage<MonoMagicFile>, IImageFormatWriter<MonoMagicFile> {

  internal const int FixedWidth = 320;
  internal const int FixedHeight = 200;
  /// <summary>
  /// A load address and 8192 bytes, of which the first 8000 are the screen.
  /// </summary>
  /// <remarks>
  /// This was 9009, which no sample is, so the only Mono Magic picture in the corpus was refused
  /// outright while RECOIL and XnView both drew it. It is 8194: two bytes of load address and eight
  /// kilobytes, the last 192 of which the screen does not reach into.
  /// </remarks>
  internal const int FileSize = 8194;

  /// <summary>Bytes of screen: 40 columns by 200 rows.</summary>
  internal const int ScreenSize = 8000;

  /// <summary>Where the screen starts, the two bytes before it being the load address.</summary>
  internal const int ScreenOffset = 2;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<MonoMagicFile>.PrimaryExtension => ".mon";
  static string[] IImageFormatMetadata<MonoMagicFile>.FileExtensions => [".mon"];
  static MonoMagicFile IImageFormatReader<MonoMagicFile>.FromSpan(ReadOnlySpan<byte> data) => MonoMagicReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MonoMagicFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<MonoMagicFile>.ToBytes(MonoMagicFile file) => MonoMagicWriter.ToBytes(file);

  public int Width => FixedWidth;
  public int Height => FixedHeight;
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Puts a screen held a character cell at a time back into rows, and vice versa.
  /// </summary>
  /// <remarks>
  /// The machine's bitmap runs eight bytes down a cell before moving to the cell beside it, which is
  /// not how rows run. The one sample matches RECOIL and XnView on every pixel read this way.
  /// </remarks>
  internal static byte[] CellsToRows(ReadOnlySpan<byte> cells) {
    var rows = new byte[ScreenSize];
    const int columns = FixedWidth / 8;

    for (var cellRow = 0; cellRow < FixedHeight / 8; ++cellRow)
      for (var cellColumn = 0; cellColumn < columns; ++cellColumn)
        for (var line = 0; line < 8; ++line)
          rows[(cellRow * 8 + line) * columns + cellColumn] = cells[(cellRow * columns + cellColumn) * 8 + line];

    return rows;
  }

  /// <summary>Puts rows back into character cells.</summary>
  internal static byte[] RowsToCells(ReadOnlySpan<byte> rows) {
    var cells = new byte[ScreenSize];
    const int columns = FixedWidth / 8;

    for (var cellRow = 0; cellRow < FixedHeight / 8; ++cellRow)
      for (var cellColumn = 0; cellColumn < columns; ++cellColumn)
        for (var line = 0; line < 8; ++line) {
          var from = (cellRow * 8 + line) * columns + cellColumn;
          cells[(cellRow * columns + cellColumn) * 8 + line] = from < rows.Length ? rows[from] : (byte)0;
        }

    return cells;
  }

  public static RawImage ToRawImage(MonoMagicFile file) {
    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static MonoMagicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
