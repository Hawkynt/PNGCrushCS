using System;
using FileFormat.Core;

namespace FileFormat.PaintShopCompressed;

/// <summary>In-memory representation of a compressed PaintShop picture (.psc).</summary>
/// <remarks>
/// A monochrome picture packed a scanline at a time rather than a byte at a time. Every command
/// produces one whole line — filled with black, filled with white, filled with one byte, filled
/// with two alternating, or stored literally — except the two that repeat the line above, which is
/// what makes a drawing with flat horizontal areas cost almost nothing.
/// <para/>
/// Working in lines rather than runs means the encoder never has to look across a line boundary,
/// and it is why a two-byte alternating fill earns a command of its own: a dither pattern is one
/// line repeated, and a repeated line is already free.
/// </remarks>
public readonly record struct PaintShopCompressedFile
  : IImageFormatReader<PaintShopCompressedFile>, IImageToRawImage<PaintShopCompressedFile>,
    IImageFromRawImage<PaintShopCompressedFile>, IImageFormatWriter<PaintShopCompressedFile> {

  static byte[] IImageFormatWriter<PaintShopCompressedFile>.ToBytes(PaintShopCompressedFile file)
    => PaintShopCompressedWriter.ToBytes(file);

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "tm89";

  /// <summary>Offset of the commands.</summary>
  public const int CommandsOffset = 14;

  /// <summary>The byte that closes the command stream.</summary>
  public const byte Terminator = 255;

  static string IImageFormatMetadata<PaintShopCompressedFile>.PrimaryExtension => ".psc";
  static string[] IImageFormatMetadata<PaintShopCompressedFile>.FileExtensions => [".psc"];
  static PaintShopCompressedFile IImageFormatReader<PaintShopCompressedFile>.FromSpan(ReadOnlySpan<byte> data)
    => PaintShopCompressedReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PaintShopCompressedFile>.VideoModes => [
    new("PaintShop", [(new IntegerRange(1, 640), new IntegerRange(1, 400))], [2])
  ];

  /// <summary>The unpacked bitmap.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(PaintShopCompressedFile file) {
    var bitmap = file.Bitmap ?? [];
    var stride = (file.Width + 7) >> 3;
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 3);
      if (at < bitmap.Length && ((bitmap[at] >> (~x & 7)) & 1) != 0)
        pixels[y * file.Width + x] = 1;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = 2,
    };
  }

  /// <summary>The largest picture the two-byte sizes and the program between them allow.</summary>
  public const int MaximumWidth = 640;

  /// <summary>Rows the format allows at most.</summary>
  public const int MaximumHeight = 400;

  /// <summary>Builds a bilevel picture, which is all this format holds.</summary>
  public static PaintShopCompressedFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Min(image.Width, MaximumWidth);
    var height = Math.Min(image.Height, MaximumHeight);
    if (width < 1 || height < 1)
      throw new ArgumentOutOfRangeException(nameof(image), "A picture needs at least one pixel.");

    var scaled = image.Width == width && image.Height == height ? image : image.SampleTo(width, height);

    return new() {
      Width = width,
      Height = height,
      Bitmap = BilevelRows.Pack(BilevelRows.Threshold(scaled, setWhenDark: true), width, height),
    };
  }
}
