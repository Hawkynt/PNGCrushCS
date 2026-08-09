using System;
using FileFormat.Core;

namespace FileFormat.SmartFax;

/// <summary>In-memory representation of a SmartFax page (.001, .smf).</summary>
/// <remarks>
/// The reader that stood here required four bytes of <c>SMFX</c> and read uncompressed rows behind a
/// ten-byte header. Those four bytes appear in no file and in no other reader; they were invented, and
/// the format they described agreed with nothing but itself. What XnView reads under this name is a
/// different file, and this is now that file.
/// <para/>
/// Five characters of <c>FAX1D</c>, which name the coding as much as the format. A little-endian word at
/// 5 gives the row length in bytes and the width is that times eight — the format has no way of stating
/// a width that is not a whole number of bytes. Two bytes are skipped; the byte at 9 chooses the vertical
/// resolution, zero meaning 100 dots an inch and anything else 200. Six more bytes are skipped and the
/// coding begins at 16. It is Group 3 one-dimensional with the bits running from the bottom of each byte
/// upwards. Nothing states a height: it is however many rows the coding holds.
/// <para/>
/// A page that ends with the coding's end-of-line word is read here as the rows it carries; the converter
/// counts that last separator as one further, blank row. Nothing this writes ends with a bare separator
/// that is not a row, so the two agree on everything written here.
/// </remarks>
public readonly record struct SmartFaxFile
  : IImageFormatReader<SmartFaxFile>, IImageToRawImage<SmartFaxFile>,
    IImageFromRawImage<SmartFaxFile>, IImageFormatWriter<SmartFaxFile> {

  /// <summary>The five characters a page opens with.</summary>
  public static ReadOnlySpan<byte> Signature => "FAX1D"u8;

  /// <summary>Where the row length in bytes stands, as a little-endian word.</summary>
  public const int BytesPerRowOffset = 5;

  /// <summary>Where the byte choosing the vertical resolution stands.</summary>
  public const int ResolutionOffset = 9;

  /// <summary>The two resolutions that byte chooses between.</summary>
  public const int CoarseResolution = 100, FineResolution = 200;

  /// <summary>How long the header is, which is where the coding begins.</summary>
  public const int HeaderSize = 16;

  /// <summary>The most rows this will decode.</summary>
  public const int MaxRows = 4300;

  /// <summary>The smallest file that can carry a header and something behind it.</summary>
  public const int MinFileSize = HeaderSize + 1;

  static string IImageFormatMetadata<SmartFaxFile>.PrimaryExtension => ".smf";
  static string[] IImageFormatMetadata<SmartFaxFile>.FileExtensions => [".smf", ".001"];
  static SmartFaxFile IImageFormatReader<SmartFaxFile>.FromSpan(ReadOnlySpan<byte> data) => SmartFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<SmartFaxFile>.ToBytes(SmartFaxFile file) => SmartFaxWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<SmartFaxFile>.VideoModes => [
    new("Default", [(new IntegerRange(8, 65528, step: 8), IntegerRange.Any)], [2])
  ];

  static bool? IImageFormatMetadata<SmartFaxFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  /// <summary>Pixels across, which is the stated row length times eight.</summary>
  public int Width { get; init; }

  /// <summary>Rows, which is however many the coding held.</summary>
  public int Height { get; init; }

  /// <summary>Dots an inch down: 100 or 200.</summary>
  public int VerticalResolution { get; init; }

  /// <summary>Packed rows, a set bit being ink.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(SmartFaxFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>Reduces a picture to the two tones a fax page holds, rounded out to a whole number of bytes.</summary>
  public static SmartFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The format can only state a width in whole bytes, so a picture that is not a multiple of eight
    // across is laid on paper that is.
    var width = (image.Width + 7) & ~7;

    return new() {
      Width = width,
      Height = image.Height,
      VerticalResolution = FineResolution,
      PixelData = MonochromePage.Encode(image, width, image.Height, inkIsWhite: false),
    };
  }
}
