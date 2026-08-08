using System;
using FileFormat.Core;

namespace FileFormat.GephardHires;

/// <summary>In-memory representation of a Gephard Hires Graphics picture (.ghg).</summary>
/// <remarks>
/// Three bytes of header — the width as a little-endian word, then the height as one byte — and
/// after that the bitmap, one bit a pixel, most significant bit leftmost. A set bit is ink.
/// <para/>
/// What was here before read the file as a Commodore 64 screen: a two-byte load address, then 8000
/// bytes of bitmap and 1000 of screen memory at a fixed 320 by 200, and it refused anything under
/// 9002 bytes. The one real sample is 2923, which is 3 plus 20 bytes a row for 146 rows, and states
/// 158 by 146 in its first three bytes. None of the C64 model was in it.
/// </remarks>
public readonly record struct GephardHiresFile
  : IImageFormatReader<GephardHiresFile>, IImageToRawImage<GephardHiresFile>,
    IImageFromRawImage<GephardHiresFile>, IImageFormatWriter<GephardHiresFile> {

  static string IImageFormatMetadata<GephardHiresFile>.PrimaryExtension => ".ghg";
  static string[] IImageFormatMetadata<GephardHiresFile>.FileExtensions => [".ghg"];
  static GephardHiresFile IImageFormatReader<GephardHiresFile>.FromSpan(ReadOnlySpan<byte> data) => GephardHiresReader.FromSpan(data);
  static byte[] IImageFormatWriter<GephardHiresFile>.ToBytes(GephardHiresFile file) => GephardHiresWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GephardHiresFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, MaxWidth), new IntegerRange(1, MaxHeight))], [ColorCount])
  ];

  /// <summary>The width as a word, then the height as a byte.</summary>
  public const int HeaderSize = 3;

  /// <summary>The widest the mode goes.</summary>
  public const int MaxWidth = 320;

  /// <summary>The tallest, which is also all a single byte can hold of it.</summary>
  public const int MaxHeight = 200;

  public const int ColorCount = 2;

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The bitmap, one bit a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// The two shades a Gephard picture is drawn in, which are not black and white.
  /// </summary>
  /// <remarks>
  /// Measured off the reference decoder: a clear bit is 0xCC and a set one 0x22, the two greys the
  /// machine's luminance 12 and 2 give. Drawn as plain black and white the arrangement comes out
  /// right to the pixel — 18483 of one and 4585 of the other, matching exactly — and every pixel
  /// still counts as a disagreement, which is what made the shades worth measuring rather than
  /// assuming.
  /// </remarks>
  private static ReadOnlySpan<byte> Shades => [0xCC, 0xCC, 0xCC, 0x22, 0x22, 0x22];

  public static RawImage ToRawImage(GephardHiresFile file)
    => MonochromePage.Decode(file.PixelData ?? [], file.Width, file.Height, inkIsWhite: true, Shades);

  public static GephardHiresFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > MaxWidth || image.Height is < 1 or > MaxHeight)
      throw new ArgumentException(
        $"A Gephard Hires picture is at most {MaxWidth}x{MaxHeight}; got {image.Width}x{image.Height}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: true),
    };
  }
}
