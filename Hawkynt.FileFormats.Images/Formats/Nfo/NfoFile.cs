using System;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.Nfo;

/// <summary>
/// NFO/DIZ "info file": a plain CP437 text file (no escape codes, no attribute bytes) traditionally
/// hand-laid with box-drawing characters in the 0x80-0xDF range. Common widths: 80 (NFO) and 45 (DIZ).
/// </summary>
[FormatMimeType("text/x-nfo")]
public readonly record struct NfoFile : IImageFormatReader<NfoFile>, IImageFormatWriter<NfoFile>, IImageToRawImage<NfoFile>, IImageFromRawImage<NfoFile> {

  static string IImageFormatMetadata<NfoFile>.PrimaryExtension => ".nfo";
  static string[] IImageFormatMetadata<NfoFile>.FileExtensions => [".nfo", ".diz"];
  static NfoFile IImageFormatReader<NfoFile>.FromSpan(ReadOnlySpan<byte> data) => NfoReader.FromSpan(data);
  static byte[] IImageFormatWriter<NfoFile>.ToBytes(NfoFile file) => NfoWriter.ToBytes(file);

  /// <summary>
  /// Whole cells of the font, in two colours.
  /// </summary>
  /// <remarks>
  /// Two, not the sixteen its neighbours take: an NFO stores characters and no attribute bytes, so
  /// every cell is drawn light grey on black and there is nowhere to say otherwise.
  /// </remarks>
  static VideoMode[] IImageFormatMetadata<NfoFile>.VideoModes => [
    new("Text", [TextModeGrid.Dimensions], [2])
  ];

  /// <summary>Default column count used when no width is detected (classic 80-column scene NFO).</summary>
  public const int DefaultColumnCount = 80;

  /// <summary>Column width used for rendering — derived from the longest line at load time.</summary>
  public int ColumnCount { get; init; }
  /// <summary>Row count = number of lines.</summary>
  public int RowCount { get; init; }
  /// <summary>Raw CP437 grid (ColumnCount × RowCount). Space (0x20) for padding.</summary>
  public byte[] CellBytes { get; init; }

  public static RawImage ToRawImage(NfoFile file) {
    ArgumentNullException.ThrowIfNull(file.CellBytes);
    var cells = new TextCell[file.ColumnCount * file.RowCount];
    for (var i = 0; i < cells.Length; ++i)
      cells[i] = new TextCell(file.CellBytes[i], Foreground: 7, Background: 0);
    var screen = new TextScreen {
      ColumnCount = file.ColumnCount,
      RowCount = file.RowCount,
      Cells = cells,
    };
    var img = TextScreenRenderer.Render(screen);
    return new() { Width = img.Width, Height = img.Height, Format = PixelFormat.Rgb24, PixelData = img.PixelData };
  }

  public static NfoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    var font = BitmapFont.Default;
    if (image.Width % font.CellWidth != 0 || image.Height % font.CellHeight != 0)
      throw new ArgumentException($"NFO requires the source image to align to the {font.CellWidth}×{font.CellHeight} text cell grid.", nameof(image));
    var cols = image.Width / font.CellWidth;
    var rows = image.Height / font.CellHeight;
    var screen = TextScreenQuantizer.FromRgb24(image.PixelData, image.Width, image.Height, cols, rows, font);
    var bytes = new byte[cols * rows];
    for (var i = 0; i < bytes.Length; ++i) bytes[i] = screen.Cells[i].CodePoint;
    return new NfoFile { ColumnCount = cols, RowCount = rows, CellBytes = bytes };
  }
}
