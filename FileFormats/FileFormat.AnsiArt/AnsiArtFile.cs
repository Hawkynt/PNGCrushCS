using System;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.AnsiArt;

/// <summary>
/// ANSI art file (.ans): CP437 text mixed with CSI escape sequences for colour and cursor positioning.
/// Optional 128-byte SAUCE record (with 'COMNT' chain) at end-of-file carries metadata (title, author, group).
/// </summary>
[FormatMimeType("text/x-ansi")]
public readonly record struct AnsiArtFile : IImageFormatReader<AnsiArtFile>, IImageFormatWriter<AnsiArtFile>, IImageToRawImage<AnsiArtFile>, IImageFromRawImage<AnsiArtFile> {

  static string IImageFormatMetadata<AnsiArtFile>.PrimaryExtension => ".ans";
  static string[] IImageFormatMetadata<AnsiArtFile>.FileExtensions => [".ans", ".ansi"];
  static AnsiArtFile IImageFormatReader<AnsiArtFile>.FromSpan(ReadOnlySpan<byte> data) => AnsiArtReader.FromSpan(data);
  static byte[] IImageFormatWriter<AnsiArtFile>.ToBytes(AnsiArtFile file) => AnsiArtWriter.ToBytes(file);

  public int ColumnCount { get; init; }
  public int RowCount { get; init; }
  public TextCell[] Cells { get; init; }

  /// <summary>Optional SAUCE record (128 bytes) trailing the art; null if absent.</summary>
  public byte[]? SauceRecord { get; init; }

  public static RawImage ToRawImage(AnsiArtFile file) {
    ArgumentNullException.ThrowIfNull(file.Cells);
    var screen = new TextScreen { ColumnCount = file.ColumnCount, RowCount = file.RowCount, Cells = file.Cells };
    var img = TextScreenRenderer.Render(screen);
    return new() { Width = img.Width, Height = img.Height, Format = PixelFormat.Rgb24, PixelData = img.PixelData };
  }

  public static AnsiArtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("ANSI quantizer expects PixelFormat.Rgb24 — convert first via PixelConverter.", nameof(image));
    var font = BitmapFont.Default;
    if (image.Width % font.CellWidth != 0 || image.Height % font.CellHeight != 0)
      throw new ArgumentException($"ANSI requires the source image to align to the {font.CellWidth}×{font.CellHeight} text-cell grid.", nameof(image));
    var cols = image.Width / font.CellWidth;
    var rows = image.Height / font.CellHeight;
    var screen = TextScreenQuantizer.FromRgb24(image.PixelData, image.Width, image.Height, cols, rows, font);
    return new AnsiArtFile { ColumnCount = cols, RowCount = rows, Cells = screen.Cells };
  }
}
