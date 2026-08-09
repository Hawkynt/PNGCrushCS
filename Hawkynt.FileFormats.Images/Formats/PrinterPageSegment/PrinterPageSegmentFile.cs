using System;
using FileFormat.Core;

namespace FileFormat.PrinterPageSegment;

/// <summary>In-memory representation of an IBM printer page segment.</summary>
/// <remarks>
/// A page segment is a piece of a printed page held on its own: MO:DCA structured fields, each one
/// introduced by <c>5A</c>, a big-endian length covering the eight bytes behind it, and a three-byte
/// type. What is inside is IM1 — the older of the two image architectures IBM's printers take, one
/// bit a pixel with no coding at all — and NOT the IOCA that shares the file family. That distinction
/// is the whole reason this row could not be closed by the IOCA reader already here: an IOCA page
/// segment built to test it is read by XnView under its <c>ioca</c> name and refused outright under
/// <c>pseg</c>, the two being different readers over different field types.
/// <para/>
/// The picture is described once and then placed in pieces. The image input descriptor gives the
/// whole picture's size and the width of a cell; each image cell position says where the next cell
/// goes, in pels across and rows down; and each image picture data field is that many bytes of raw
/// bits a row, copied straight in. So a segment is a mosaic, and a reader that ignored the cell
/// positions would draw every piece on top of the first.
/// <para/>
/// A set bit is ink. The raster starts out all zero, which is paper, so anything the fields do not
/// reach stays white rather than black — and the fill rectangle a cell position may carry writes
/// paper too, clearing what an earlier cell put there.
/// </remarks>
public readonly record struct PrinterPageSegmentFile : IImageFormatReader<PrinterPageSegmentFile>, IImageToRawImage<PrinterPageSegmentFile>, IImageFromRawImage<PrinterPageSegmentFile>, IImageFormatWriter<PrinterPageSegmentFile> {

  static string IImageFormatMetadata<PrinterPageSegmentFile>.PrimaryExtension => ".pse";

  /// <summary>Also .psg, which the catalogue lists beside it and its own summary leaves out.</summary>
  static string[] IImageFormatMetadata<PrinterPageSegmentFile>.FileExtensions => [".pse", ".psg"];

  static PrinterPageSegmentFile IImageFormatReader<PrinterPageSegmentFile>.FromSpan(ReadOnlySpan<byte> data) => PrinterPageSegmentReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PrinterPageSegmentFile>.VideoModes => [
    new("IM1", [(IntegerRange.Any, IntegerRange.Any)], [2]),
  ];
  static byte[] IImageFormatWriter<PrinterPageSegmentFile>.ToBytes(PrinterPageSegmentFile file) => PrinterPageSegmentWriter.ToBytes(file);

  /// <summary>
  /// Whether the file opens the way a page segment must: <c>5A</c>, and a type this reader walks.
  /// </summary>
  /// <remarks>
  /// One byte is no signature — <c>5A</c> is a letter Z and the start of plenty of other things — so
  /// the type behind it is checked too. That is also what the reader itself does first, and it is
  /// what turns an IOCA segment away.
  /// </remarks>
  static bool? IImageFormatMetadata<PrinterPageSegmentFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < PrinterPageSegmentReader.IntroductionSize)
      return null;
    if (header[0] != PrinterPageSegmentReader.FieldIntroducer)
      return false;

    return PrinterPageSegmentReader.IsFirstPassType((header[3] << 16) | (header[4] << 8) | header[5]);
  }

  /// <summary>Largest picture the original will allocate, either way round.</summary>
  public const int MaximumExtent = 0x927BF;

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The picture, one bit a pixel from the top-left corner, most significant bit first.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes a row takes.</summary>
  public int Stride => (this.Width + 7) / 8;

  /// <summary>Paper, then ink — the order a set bit being black requires.</summary>
  private static readonly byte[] _Palette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(PrinterPageSegmentFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _Palette[..],
    PaletteCount = 2,
  };

  /// <summary>
  /// Reduces the picture to ink and paper, at a width the format can state.
  /// </summary>
  /// <remarks>
  /// Both the descriptor's width and the cell's have to divide by eight — the original refuses a file
  /// that breaks it and so does the reader here — so a picture whose width does not is widened to the
  /// next multiple of eight and resampled into it rather than turned away. Widening rather than
  /// narrowing because the alternative throws away a column of a picture only seven pixels wide.
  /// <para/>
  /// A set bit is ink, so the threshold sets the bit on the DARK pixel. Getting that backwards
  /// produces a negative, which on a scanned page is obvious and on a line drawing is not.
  /// </remarks>
  public static PrinterPageSegmentFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Min((image.Width + 7) & ~7, MaximumExtent & ~7);
    var height = Math.Min(image.Height, MaximumExtent);
    var source = width == image.Width && height == image.Height ? image : image.SampleTo(width, height);

    return new() {
      Width = width,
      Height = height,
      PixelData = BilevelRows.Pack(BilevelRows.Threshold(source, setWhenDark: true), width, height),
    };
  }
}
