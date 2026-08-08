using System;
using FileFormat.Core;

namespace FileFormat.FastgraphPixelRun;

/// <summary>In-memory representation of a Fastgraph pixel run picture (.prf).</summary>
/// <remarks>
/// Fastgraph's pixel run files are ordinarily headerless — a bare run-length stream that needs the
/// program that wrote it to say how wide the picture is. One family of them is not, and it is the one
/// here: it opens with <c>FASTGRAF</c> written a byte to a word, low byte first and the high byte
/// zero, and it carries its size the same way. Thirteen bytes of header stored as twenty-six: the
/// eight of the name, the largest x and the largest y as two bytes each, and a spare.
/// <para/>
/// After that come pairs — a colour index and how many pixels in a row take it, one to 255 — filling
/// the picture from the bottom row upwards and left to right within each row.
/// <para/>
/// What settles this is that the pairs account for the stated size to the pixel. Across the fourteen
/// files there are, the run counts add up to exactly (xmax + 1) × (ymax + 1) — 320 × 200 for thirteen
/// of them and 160 × 200 for the fourteenth — and the last pair is the last two bytes of the file. The
/// row order is not deduced but read: filled bottom-up the menus and captions in these game screens
/// are the right way up and legible, and filled top-down they are upside down.
/// <para/>
/// There is no palette. Fastgraph programs loaded one through the library before drawing and none of
/// it was written to the file, so the picture comes back indexed against
/// <see cref="VgaPalette.Default256"/> — the colours a VGA holds until something changes them. The
/// shapes are the file's; the colours are not, and where the real ones matter they have to be supplied
/// from outside it.
/// </remarks>
[FormatMagicBytes([
  (byte)'F', 0, (byte)'A', 0, (byte)'S', 0, (byte)'T', 0,
  (byte)'G', 0, (byte)'R', 0, (byte)'A', 0, (byte)'F', 0
])]
public readonly record struct FastgraphPixelRunFile
  : IImageFormatReader<FastgraphPixelRunFile>, IImageToRawImage<FastgraphPixelRunFile>,
    IImageFromRawImage<FastgraphPixelRunFile>, IImageFormatWriter<FastgraphPixelRunFile> {

  /// <summary>The name every one of these opens with, a byte to a word.</summary>
  public static ReadOnlySpan<byte> Magic => [
    (byte)'F', 0, (byte)'A', 0, (byte)'S', 0, (byte)'T', 0,
    (byte)'G', 0, (byte)'R', 0, (byte)'A', 0, (byte)'F', 0
  ];

  /// <summary>Thirteen bytes of header written as twenty-six.</summary>
  public const int HeaderSize = 26;

  /// <summary>The longest run a count byte can hold.</summary>
  public const int MaxRun = 255;

  /// <summary>Bigger than any Fastgraph mode and it keeps a false match cheap.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<FastgraphPixelRunFile>.PrimaryExtension => ".prf";
  static string[] IImageFormatMetadata<FastgraphPixelRunFile>.FileExtensions => [".prf"];
  static FastgraphPixelRunFile IImageFormatReader<FastgraphPixelRunFile>.FromSpan(ReadOnlySpan<byte> data)
    => FastgraphPixelRunReader.FromSpan(data);
  static byte[] IImageFormatWriter<FastgraphPixelRunFile>.ToBytes(FastgraphPixelRunFile file)
    => FastgraphPixelRunWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FastgraphPixelRunFile>.VideoModes => [
    new("Pixel runs", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))], [256])
  ];

  /// <summary>The picture's width, one more than the largest x the header states.</summary>
  public int Width { get; init; }

  /// <summary>The picture's height, one more than the largest y the header states.</summary>
  public int Height { get; init; }

  /// <summary>One colour index per pixel, rows top-down after the bottom-up runs are unwound.</summary>
  public byte[] Pixels { get; init; }

  public static RawImage ToRawImage(FastgraphPixelRunFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.Pixels ?? [],
    Palette = VgaPalette.Default256,
    PaletteCount = 256,
  };

  public static FastgraphPixelRunFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > MaxDimension || image.Height is < 1 or > MaxDimension)
      throw new ArgumentException($"A Fastgraph picture is at most {MaxDimension}x{MaxDimension} and this is {image.Width}x{image.Height}.", nameof(image));

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, VgaPalette.Default256);

    return new() { Width = indexed.Width, Height = indexed.Height, Pixels = indexed.PixelData };
  }
}
