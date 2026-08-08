using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.JigsawPuzzle;

/// <summary>In-memory representation of a jigsaw puzzle picture (.jig).</summary>
/// <remarks>
/// An ordinary Windows bitmap with the two letters it opens with changed: <c>JG</c> where a BMP says
/// <c>BM</c>. Everything after those two bytes is a <c>BITMAPFILEHEADER</c> followed by a
/// <c>BITMAPINFOHEADER</c>, unaltered — the stated picture offset, the stated size and the stated
/// depth are all read where a BMP keeps them.
/// <para/>
/// What is appended after the bitmap is the puzzle: the file states its own length in the header,
/// and in all eleven samples that length is exactly the picture offset plus height times the padded
/// row length, with the remaining bytes carrying the piece layout, the author and a description —
/// one reads "a 256-color jigsaw puzzle with 11x6 pieces. The pieces are 29-pixels across". So the
/// bitmap's own arithmetic says where it ends and the puzzle begins, to the byte, and the reader
/// refuses the file unless it does.
/// <para/>
/// That check is what makes this more than a renamed BMP: two letters alone would let anything
/// beginning <c>JG</c> through, whereas a file whose stated length does not account for its own
/// pixels is not one of these.
/// </remarks>
public readonly record struct JigsawPuzzleFile
  : IImageFormatReader<JigsawPuzzleFile>, IImageToRawImage<JigsawPuzzleFile>,
    IImageFromRawImage<JigsawPuzzleFile>, IImageFormatWriter<JigsawPuzzleFile> {

  /// <summary>The two letters every one of these opens with, where a bitmap says <c>BM</c>.</summary>
  public static ReadOnlySpan<byte> Magic => [(byte)'J', (byte)'G'];

  /// <summary>The two letters a Windows bitmap opens with, which is what the reader puts back.</summary>
  internal static ReadOnlySpan<byte> BitmapMagic => [(byte)'B', (byte)'M'];

  /// <summary>A <c>BITMAPFILEHEADER</c>, which is where the bitmap this carries starts.</summary>
  internal const int FileHeaderSize = 14;

  /// <summary>Where the header states the length of the bitmap, as a little-endian long.</summary>
  internal const int BitmapLengthAt = 2;

  /// <summary>Where the header states the offset the pixels begin at, as a little-endian long.</summary>
  internal const int PixelOffsetAt = 10;

  /// <summary>Where the <c>BITMAPINFOHEADER</c> begins, and where within it the size and depth stand.</summary>
  internal const int InfoHeaderAt = 14, WidthAt = 18, HeightAt = 22, BitsPerPixelAt = 28, CompressionAt = 30;

  static string IImageFormatMetadata<JigsawPuzzleFile>.PrimaryExtension => ".jig";
  static string[] IImageFormatMetadata<JigsawPuzzleFile>.FileExtensions => [".jig"];
  static JigsawPuzzleFile IImageFormatReader<JigsawPuzzleFile>.FromSpan(ReadOnlySpan<byte> data) => JigsawPuzzleReader.FromSpan(data);
  static byte[] IImageFormatWriter<JigsawPuzzleFile>.ToBytes(JigsawPuzzleFile file) => JigsawPuzzleWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<JigsawPuzzleFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>Two letters and the arithmetic that has to hold behind them.</summary>
  /// <remarks>
  /// <c>JG</c> alone is two bytes and would claim anything that happens to begin with them, so what
  /// is matched is the header accounting for itself: the stated bitmap length being the stated pixel
  /// offset plus the height times the padded row length. That needs only the first thirty-two bytes,
  /// which is what signature matching is given.
  /// </remarks>
  static bool? IImageFormatMetadata<JigsawPuzzleFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < BitsPerPixelAt + 2 || !header[..Magic.Length].SequenceEqual(Magic))
      return null;

    var bitmapLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[BitmapLengthAt..]);
    var pixelOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[PixelOffsetAt..]);
    var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[WidthAt..]);
    var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[HeightAt..]);
    var bitsPerPixel = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[BitsPerPixelAt..]);

    var rows = height < 0 ? -(long)height : height;
    if (width < 1 || rows < 1 || bitsPerPixel is < 1 or > 32)
      return null;

    var stride = ((long)width * bitsPerPixel + 31) / 32 * 4;
    return pixelOffset + rows * stride == bitmapLength ? true : null;
  }

  /// <summary>Pixels across, as the bitmap header states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the bitmap header states.</summary>
  public int Height { get; init; }

  /// <summary>Bits a pixel, as the bitmap header states.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>The bitmap this carries, with <c>BM</c> put back where the file says <c>JG</c>.</summary>
  public byte[] Embedded { get; init; }

  /// <summary>What follows the bitmap: the piece layout, the author and the description.</summary>
  public byte[] Puzzle { get; init; }

  public static RawImage ToRawImage(JigsawPuzzleFile file)
    => BmpFile.ToRawImage(BmpReader.FromBytes(file.Embedded ?? throw new InvalidDataException("A jigsaw puzzle picture carries no bitmap.")));

  public static JigsawPuzzleFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var embedded = BmpWriter.ToBytes(BmpFile.FromRawImage(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = RawImage.BitsPerPixel(image.Format),
      Embedded = embedded,
      Puzzle = [],
    };
  }
}
