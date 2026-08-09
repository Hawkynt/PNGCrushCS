using System;
using FileFormat.Core;

namespace FileFormat.RicohIs30;

/// <summary>In-memory representation of a Ricoh IS30 scan (.pig).</summary>
/// <remarks>
/// The IS30 was a Ricoh document scanner and nothing describes what its software wrote. The layout
/// comes from XnView's own converter and was confirmed by construction: a file built to it is reported
/// at the size and depth it was built with and hands back the pixels that went in.
/// <para/>
/// What makes it unusual is that the header is half binary and half text. A byte of one and a byte of
/// zero open it; the third byte chooses the depth, one meaning one bit a pixel and anything else two.
/// The three numbers that follow are written as ASCII decimal with leading zeros and read with
/// <c>strtol</c>: three characters of resolution at 3, four characters of bytes-per-row at 6, and five
/// characters of height at 10. Two bytes are not read and the byte at 17 has to be two. The pixels
/// start at 18 and are not compressed.
/// <para/>
/// The width is not stored: it is the row length in bytes times eight divided by the depth, which is
/// how the converter derives it. At two bits a pixel the four values are a grey ramp with zero white
/// and three black.
/// </remarks>
public readonly record struct RicohIs30File : IImageFormatReader<RicohIs30File>, IImageToRawImage<RicohIs30File> {

  /// <summary>The two bytes the file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0x01, 0x00];

  /// <summary>Where the byte choosing the depth stands.</summary>
  public const int DepthSelectorOffset = 2;

  /// <summary>Where the three characters of resolution stand.</summary>
  public const int ResolutionOffset = 3, ResolutionLength = 3;

  /// <summary>Where the four characters giving the row length in bytes stand.</summary>
  public const int BytesPerRowOffset = 6, BytesPerRowLength = 4;

  /// <summary>Where the five characters of height stand.</summary>
  public const int HeightOffset = 10, HeightLength = 5;

  /// <summary>Where the byte that has to be two stands.</summary>
  public const int MarkerOffset = 17;

  /// <summary>What that byte has to be.</summary>
  public const byte MarkerValue = 2;

  /// <summary>How long the header is, which is where the pixels begin.</summary>
  public const int HeaderSize = 18;

  static string IImageFormatMetadata<RicohIs30File>.PrimaryExtension => ".pig";
  static string[] IImageFormatMetadata<RicohIs30File>.FileExtensions => [".pig"];
  static RicohIs30File IImageFormatReader<RicohIs30File>.FromSpan(ReadOnlySpan<byte> data) => RicohIs30Reader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<RicohIs30File>.VideoModes => [
    new("Bilevel", [(IntegerRange.Any, IntegerRange.Any)], [2]),
    new("Four greys", [(IntegerRange.Any, IntegerRange.Any)], [4]),
  ];

  static bool? IImageFormatMetadata<RicohIs30File>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize)
      return null;

    if (!header[..Signature.Length].SequenceEqual(Signature) || header[MarkerOffset] != MarkerValue)
      return false;

    // Two bytes and a marker are not enough on their own; the three ASCII numbers are what makes this
    // a signature rather than a coincidence, so they are required to be digits.
    for (var i = ResolutionOffset; i < HeightOffset + HeightLength; ++i)
      if (header[i] is < (byte)'0' or > (byte)'9')
        return false;

    return true;
  }

  /// <summary>Pixels across, which the format states only as a row length and a depth.</summary>
  public int Width { get; init; }

  /// <summary>Rows, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Bits a pixel: one or two.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Dots an inch, as the header states.</summary>
  public int Resolution { get; init; }

  /// <summary>The rows exactly as they stand in the file, each starting on a byte.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];
  private static readonly byte[] _FourGreyPalette = [255, 255, 255, 170, 170, 170, 85, 85, 85, 0, 0, 0];

  public static RawImage ToRawImage(RicohIs30File file) {
    var packed = file.PixelData ?? [];
    var indices = PackedRows.Unpack(packed, file.Width, file.Height, file.BitsPerPixel);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = file.BitsPerPixel == 1 ? _BlackWhitePalette[..] : _FourGreyPalette[..],
      PaletteCount = file.BitsPerPixel == 1 ? 2 : 4,
    };
  }
}
