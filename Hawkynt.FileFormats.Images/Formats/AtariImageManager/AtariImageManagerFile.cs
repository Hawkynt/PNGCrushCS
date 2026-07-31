using System;
using FileFormat.Core;

namespace FileFormat.AtariImageManager;

/// <summary>In-memory representation of an Atari Image Manager picture (.im, .col).</summary>
/// <remarks>
/// A square of greys, one byte a pixel, and nothing else — no header, no palette, not even a size.
/// The side follows from the length, and only two are possible. That is what a program working with
/// scanned images wanted: the file is the sample values as they came off the scanner, and anything
/// else would have to be stripped before processing them.
/// </remarks>
public readonly record struct AtariImageManagerFile
  : IImageFormatReader<AtariImageManagerFile>, IImageToRawImage<AtariImageManagerFile>,
    IImageFromRawImage<AtariImageManagerFile>, IImageFormatWriter<AtariImageManagerFile> {

  /// <summary>The smaller of the two sides.</summary>
  public const int SmallSize = 128;

  /// <summary>The larger of the two sides.</summary>
  public const int LargeSize = 256;

  static string IImageFormatMetadata<AtariImageManagerFile>.PrimaryExtension => ".im";
  static string[] IImageFormatMetadata<AtariImageManagerFile>.FileExtensions => [".im", ".col"];
  static AtariImageManagerFile IImageFormatReader<AtariImageManagerFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariImageManagerReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariImageManagerFile>.ToBytes(AtariImageManagerFile file)
    => AtariImageManagerWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariImageManagerFile>.VideoModes => [
    new("Image", [(SmallSize, SmallSize), (LargeSize, LargeSize)], [256])
  ];

  /// <summary>The samples, one byte per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Pixels across and down.</summary>
  public int Size { get; init; }

  public static RawImage ToRawImage(AtariImageManagerFile file) => new() {
    Width = file.Size,
    Height = file.Size,
    Format = PixelFormat.Gray8,
    PixelData = (file.PixelData ?? [])[..(file.Size * file.Size)],
  };

  public static AtariImageManagerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != image.Height || (image.Width != SmallSize && image.Width != LargeSize))
      throw new ArgumentException(
        $"Expected {SmallSize}x{SmallSize} or {LargeSize}x{LargeSize} but got {image.Width}x{image.Height}.",
        nameof(image));

    var gray = PixelConverter.Convert(image, PixelFormat.Gray8);
    return new() { PixelData = gray.PixelData[..(image.Width * image.Height)], Size = image.Width };
  }
}
