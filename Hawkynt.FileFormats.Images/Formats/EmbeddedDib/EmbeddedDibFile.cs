using System;
using FileFormat.Core;

namespace FileFormat.EmbeddedDib;

/// <summary>The Windows bitmap preview carried inside a drawing or a project file.</summary>
/// <remarks>
/// A whole family of formats are not pictures at all — CorelDRAW and its metafile, Zoner's metafile,
/// AutoDesk sketch thumbnails, IntroCAD drawings, jigsaw and button projects — and every one of them
/// carries a preview so a file chooser has something to show. That preview is a plain Windows DIB
/// dropped into the file: a 40-byte <c>BITMAPINFOHEADER</c>, its palette, and its rows bottom-up and
/// padded to four bytes, with no <c>BM</c> file header in front of it because it is not a file.
/// <para/>
/// Eight of the names on the coverage list turned out to be this one thing, which is why it is a
/// reader of its own rather than nine hard-coded offsets. It finds the header by looking for one —
/// a stated length of 40 to 124, one plane, a depth the format has, a size that is not absurd, and
/// enough bytes left after it to hold the picture it claims — and then hands the run to the BMP
/// reader with a file header put in front, so every depth, palette form and row order that reader
/// already knows comes free.
/// <para/>
/// A ninth, <c>.jig</c>, is left out deliberately. It carries a header of the right shape at 14 and
/// its picture where one would expect, but it states no colour count and keeps no palette anywhere
/// in the file — searching every offset for one, in either byte order and at three or four bytes an
/// entry, accounts for 13 of the 181 colours the tool draws. Whatever supplies the rest is not in
/// the file, so it is refused rather than drawn in the wrong colours.
/// <para/>
/// It does not write. What it read was a preview inside somebody else's file, and emitting one alone
/// would produce something no drawing program would open.
/// </remarks>
public readonly record struct EmbeddedDibFile
  : IImageFormatReader<EmbeddedDibFile>, IImageToRawImage<EmbeddedDibFile> {

  /// <summary>The shortest and longest <c>BITMAPINFOHEADER</c> this accepts.</summary>
  public const int MinHeaderSize = 40, MaxHeaderSize = 124;

  /// <summary>No picture in these previews comes near this, and it keeps a false match cheap.</summary>
  public const int MaxDimension = 20000;

  static string IImageFormatMetadata<EmbeddedDibFile>.PrimaryExtension => ".cdr";
  static string[] IImageFormatMetadata<EmbeddedDibFile>.FileExtensions =>
    [".cdr", ".cmx", ".zmf", ".skf", ".cad", ".sdg", ".ipg", ".btn"];
  static EmbeddedDibFile IImageFormatReader<EmbeddedDibFile>.FromSpan(ReadOnlySpan<byte> data)
    => EmbeddedDibReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<EmbeddedDibFile>.VideoModes => [
    new("Preview", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))])
  ];

  /// <summary>The preview, already decoded by the bitmap reader.</summary>
  public RawImage Preview { get; init; }

  /// <summary>Where in the containing file the preview was found.</summary>
  public int Offset { get; init; }

  public static RawImage ToRawImage(EmbeddedDibFile file)
    => file.Preview ?? throw new InvalidOperationException("No preview was read.");
}
