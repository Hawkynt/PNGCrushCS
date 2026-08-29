using System;
using FileFormat.Core;

namespace FileFormat.XionicsSmp;

/// <summary>In-memory representation of a Xionics SMP page (.smp).</summary>
/// <remarks>
/// Xionics made document-imaging hardware and nothing published says what its pages look like. The
/// header below came out of XnView's own converter and was put back to it field by field: a page built
/// to it is reported at the size and depth it states and hands back the pixels that were coded.
/// <para/>
/// A zero word, then the eight characters <c>Xionics </c> and the four bytes <c>F</c>, <c>1B</c>,
/// <c>7F</c> and zero. A word at 18 has to be one. The word at 28 chooses the coding; the row length in
/// bytes stands at 31 and the height at 33, both sixteen-bit and little-endian like everything else
/// here. Three more fixed bytes are required — <c>1B</c> as a word at 43, <c>19</c> at 45 and
/// <c>1A</c> at 50, each followed by a length of two and a resolution. The data begins at 70.
/// <para/>
/// The width is not stored: it is the row length times eight, which is how the converter derives it,
/// so every SMP page is a whole number of bytes wide.
/// <para/>
/// Five codings exist and three are read here. Zero is uncompressed rows; one is Group 3 one
/// dimensional and three is Group 4, both with the bits running from the bottom of each byte upwards.
/// Two is Group 3 two dimensional and anything from four upwards is a run-length scheme of the
/// vendor's own; neither is decoded here, and both are refused by name rather than drawn as something
/// else, because there is no file of either to check a reading against.
/// </remarks>
public readonly record struct XionicsSmpFile : IImageFormatReader<XionicsSmpFile>, IImageToRawImage<XionicsSmpFile>, IImageFromRawImage<XionicsSmpFile>, IImageFormatWriter<XionicsSmpFile> {

  /// <summary>The fourteen bytes the file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [
    0x00, 0x00,
    (byte)'X', (byte)'i', (byte)'o', (byte)'n', (byte)'i', (byte)'c', (byte)'s', (byte)' ',
    (byte)'F', 0x1B, 0x7F, 0x00,
  ];

  /// <summary>Where the word that has to be one stands.</summary>
  public const int OneOffset = 18;

  /// <summary>Where the word choosing the coding stands.</summary>
  public const int CompressionOffset = 28;

  /// <summary>Where the row length in bytes stands.</summary>
  public const int BytesPerRowOffset = 31;

  /// <summary>Where the height stands.</summary>
  public const int HeightOffset = 33;

  /// <summary>Where the word that has to be <c>1B</c> stands.</summary>
  public const int EscapeOffset = 43;

  /// <summary>Where the byte that has to be <c>19</c> stands, and where its resolution follows.</summary>
  public const int HorizontalTagOffset = 45, HorizontalResolutionOffset = 48;

  /// <summary>Where the byte that has to be <c>1A</c> stands, and where its resolution follows.</summary>
  public const int VerticalTagOffset = 50, VerticalResolutionOffset = 53;

  /// <summary>How long the header is, which is where the data begins.</summary>
  public const int HeaderSize = 70;

  /// <summary>The codings this reads.</summary>
  public const int CompressionNone = 0, CompressionGroup3 = 1, CompressionGroup3TwoDimensional = 2, CompressionGroup4 = 3;

  static string IImageFormatMetadata<XionicsSmpFile>.PrimaryExtension => ".smp";
  static string[] IImageFormatMetadata<XionicsSmpFile>.FileExtensions => [".smp"];
  static XionicsSmpFile IImageFormatReader<XionicsSmpFile>.FromSpan(ReadOnlySpan<byte> data) => XionicsSmpReader.FromSpan(data);
  static byte[] IImageFormatWriter<XionicsSmpFile>.ToBytes(XionicsSmpFile file) => XionicsSmpWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<XionicsSmpFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  static bool? IImageFormatMetadata<XionicsSmpFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  /// <summary>Pixels across, which is the stated row length times eight.</summary>
  public int Width { get; init; }

  /// <summary>Rows, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Which of the format's codings the page is in.</summary>
  public int Compression { get; init; }

  /// <summary>Dots an inch across and down, as the header states.</summary>
  public int HorizontalResolution { get; init; }

  /// <summary>Dots an inch down.</summary>
  public int VerticalResolution { get; init; }

  /// <summary>Packed rows, a set bit being ink.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(XionicsSmpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>Creates a Group-4 Xionics page, padding only the right edge to the byte width the format stores.</summary>
  public static XionicsSmpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height is < 1 or > 65535)
      throw new ArgumentException("Xionics SMP requires positive dimensions and at most 65535 rows.", nameof(image));

    var width = checked((image.Width + 7) & ~7);
    if (width > 65528)
      throw new ArgumentException($"Xionics SMP can hold at most 65528 pixels across after byte alignment; got {image.Width}.", nameof(image));

    var source = BilevelRows.Threshold(image, setWhenDark: true);
    var padded = new byte[checked(width * image.Height)];
    for (var y = 0; y < image.Height; ++y)
      source.AsSpan(y * image.Width, image.Width).CopyTo(padded.AsSpan(y * width));

    return new() {
      Width = width,
      Height = image.Height,
      Compression = CompressionGroup4,
      HorizontalResolution = 300,
      VerticalResolution = 300,
      PixelData = BilevelRows.Pack(padded, width, image.Height),
    };
  }
}
