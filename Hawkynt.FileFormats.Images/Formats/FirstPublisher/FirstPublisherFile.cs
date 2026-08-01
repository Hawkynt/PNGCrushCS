using System;
using FileFormat.Core;

namespace FileFormat.FirstPublisher;

/// <summary>In-memory representation of a 1st Publisher clip-art image (.art).</summary>
/// <remarks>
/// The desktop-publishing package that shipped these called them ART, and so did several unrelated
/// programs — the extension says nothing about what is in the file. This one is as plain as a
/// bilevel format gets: two sizes and the rows, with a zero word before each size that the format
/// never used for anything.
/// </remarks>
public readonly record struct FirstPublisherFile : IImageFormatReader<FirstPublisherFile>, IImageToRawImage<FirstPublisherFile>, IImageFromRawImage<FirstPublisherFile>, IImageFormatWriter<FirstPublisherFile> {

  static string IImageFormatMetadata<FirstPublisherFile>.PrimaryExtension => ".art";
  static string[] IImageFormatMetadata<FirstPublisherFile>.FileExtensions => [".art"];
  static FirstPublisherFile IImageFormatReader<FirstPublisherFile>.FromSpan(ReadOnlySpan<byte> data) => FirstPublisherReader.FromSpan(data);
  static byte[] IImageFormatWriter<FirstPublisherFile>.ToBytes(FirstPublisherFile file) => FirstPublisherWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FirstPublisherFile>.VideoModes => [
    new("Bilevel", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  /// <summary>The eight-byte header: a zero word before each of the two sizes.</summary>
  public const int HeaderSize = 8;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One bit a pixel, top bit leftmost, each row starting on a fresh byte.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>A clear bit is ink here, which is the opposite of most of this format's neighbours.</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(FirstPublisherFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData, file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static FirstPublisherFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image), "Both sizes are stored in a word.");

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = BilevelRows.Pack(BilevelRows.Threshold(image, setWhenDark: false), image.Width, image.Height),
    };
  }
}
