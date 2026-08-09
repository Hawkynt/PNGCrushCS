using System;
using FileFormat.Core;

namespace FileFormat.RicohFax;

/// <summary>In-memory representation of a Ricoh Fax page (.001, .ric).</summary>
/// <remarks>
/// The reader that stood here required four bytes of <c>RICF</c> and then read uncompressed rows behind
/// a twelve-byte header. That signature appears in no file anywhere and in no reader anywhere: it was
/// invented, and the format it described agreed with nothing but itself. What XnView reads under this
/// name is a different file, and this is now that file.
/// <para/>
/// Two bytes are skipped and the fourteen characters <c>FAXNET / RICOH</c> stand at offset 2; the
/// converter refuses anything else. The page begins at offset 256. Nothing in the header states a size:
/// the width is fixed at 1728, which is the standard fax scan line, and the height is however many rows
/// the coding turns out to hold. The coding is Group 3 one-dimensional with the bits running from the
/// bottom of each byte upwards, which is what makes it look like noise if read the ordinary way round.
/// <para/>
/// Rows are separated by the coding's own end-of-line word. A page that ends with one is read here as
/// the rows it carries; the converter, whose row loop needs a further byte before it will commit a row,
/// reports one fewer for a page that ends without one. Everything this writes ends with the separator,
/// so the two agree on anything written here and on any page that ends the way a fax page ends.
/// </remarks>
public readonly record struct RicohFaxFile
  : IImageFormatReader<RicohFaxFile>, IImageToRawImage<RicohFaxFile>,
    IImageFromRawImage<RicohFaxFile>, IImageFormatWriter<RicohFaxFile> {

  /// <summary>Where the fourteen characters that identify the file stand.</summary>
  public const int SignatureOffset = 2;

  /// <summary>Those fourteen characters.</summary>
  public static ReadOnlySpan<byte> Signature => "FAXNET / RICOH"u8;

  /// <summary>How long the header is, which is where the page begins.</summary>
  public const int HeaderSize = 256;

  /// <summary>The scan line this format is always coded at.</summary>
  public const int PageWidth = 1728;

  /// <summary>The most rows the converter will decode, and the most this will.</summary>
  public const int MaxRows = 4300;

  /// <summary>The smallest file that can carry a header and something behind it.</summary>
  public const int MinFileSize = HeaderSize + 1;

  static string IImageFormatMetadata<RicohFaxFile>.PrimaryExtension => ".ric";
  static string[] IImageFormatMetadata<RicohFaxFile>.FileExtensions => [".ric", ".001"];
  static RicohFaxFile IImageFormatReader<RicohFaxFile>.FromSpan(ReadOnlySpan<byte> data) => RicohFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<RicohFaxFile>.ToBytes(RicohFaxFile file) => RicohFaxWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<RicohFaxFile>.VideoModes => [
    new("Fax", [(new IntegerRange(PageWidth, PageWidth), IntegerRange.Any)], [2])
  ];

  static bool? IImageFormatMetadata<RicohFaxFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < SignatureOffset + Signature.Length)
      return null;

    return header.Slice(SignatureOffset, Signature.Length).SequenceEqual(Signature);
  }

  /// <summary>Pixels across, which this format never states because it never varies.</summary>
  public int Width => PageWidth;

  /// <summary>Rows, which is however many the coding held.</summary>
  public int Height { get; init; }

  /// <summary>Packed rows, a set bit being ink.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(RicohFaxFile file) => new() {
    Width = PageWidth,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], PageWidth, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>Reduces a picture to the two tones a fax page holds, at the one width it holds them at.</summary>
  public static RicohFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The page has one width and only one, so a narrower picture is laid on white paper and a wider one
    // is cut; a fax machine could not have sent the rest either.
    return new() {
      Height = image.Height,
      PixelData = MonochromePage.Encode(image, PageWidth, image.Height, inkIsWhite: false),
    };
  }
}
