using System;
using FileFormat.Core;

namespace FileFormat.Iss;

/// <summary>An ISS picture (.iss): eight characters of signature, a half-kilobyte header and plain rows.</summary>
/// <remarks>
/// Nothing published describes this name, so the layout came from XnView's own reader. A file opens
/// with the eight characters <c>3KCBIMSP</c>, then a big-endian word this does not read, then the kind
/// of picture, then another word and a long word it does not read either, then the height and the
/// width as big-endian long words. Everything up to byte 512 is header; the rows begin there.
/// <para/>
/// The kind is one or two and nothing else is accepted. Two is eight bits a pixel with the rows as
/// wide as the picture. One is a single bit a pixel whose rows are padded to a multiple of thirty-two
/// bytes — a 300-pixel-wide file came back with rows of sixty-four bytes, which is 256 bits rounded up
/// to the next 256 — and the leftmost pixel is the top bit of the first byte.
/// <para/>
/// Both kinds count upwards from white: XnView asked for the grey ramp that has white at zero, and a
/// file whose bytes ran 0, 40, 80 came back as 255, 215, 175. So a sample of zero is white and a sample
/// of 255 is black, and in the one-bit kind a set bit is black.
/// </remarks>
[FormatMagicBytes([0x33, 0x4B, 0x43, 0x42, 0x49, 0x4D, 0x53, 0x50])]
public readonly record struct IssFile
  : IImageFormatReader<IssFile>, IImageToRawImage<IssFile> {

  /// <summary>The eight characters a file opens with.</summary>
  public static ReadOnlySpan<byte> Magic => "3KCBIMSP"u8;

  /// <summary>Where the rows begin.</summary>
  public const int PixelsOffset = 512;

  /// <summary>The kind that carries a single bit a pixel.</summary>
  public const int MonochromeKind = 1;

  /// <summary>The kind that carries eight bits a pixel.</summary>
  public const int GrayscaleKind = 2;

  /// <summary>How many bytes the one-bit kind pads its rows to.</summary>
  public const int MonochromeRowAlignment = 32;

  static string IImageFormatMetadata<IssFile>.PrimaryExtension => ".iss";
  static string[] IImageFormatMetadata<IssFile>.FileExtensions => [".iss"];
  static IssFile IImageFormatReader<IssFile>.FromSpan(ReadOnlySpan<byte> data) => IssReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<IssFile>.VideoModes => [
    new("ISS", [(IntegerRange.Any, IntegerRange.Any)], [2, 256])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Which of the two kinds the file holds.</summary>
  public int Kind { get; init; }

  /// <summary>The rows as they stand in the file, padding and all.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>How many bytes one row takes, padding included.</summary>
  public int BytesPerRow => RowStride(this.Kind, this.Width);

  /// <summary>How many bytes one row of the given kind takes.</summary>
  public static int RowStride(int kind, int width)
    => kind == MonochromeKind
      ? (width + 255) / 256 * MonochromeRowAlignment
      : width;

  public static RawImage ToRawImage(IssFile file) {
    var source = file.PixelData;
    if (source == null)
      throw new InvalidOperationException("No ISS picture was read.");

    var width = file.Width;
    var height = file.Height;
    var stride = file.BytesPerRow;
    var pixels = new byte[width * height];

    if (file.Kind == MonochromeKind)
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = ((source[y * stride + (x >> 3)] >> (~x & 7)) & 1) != 0 ? (byte)0 : (byte)255;
    else
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = (byte)(255 - source[y * stride + x]);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Gray8,
      PixelData = pixels,
    };
  }
}
