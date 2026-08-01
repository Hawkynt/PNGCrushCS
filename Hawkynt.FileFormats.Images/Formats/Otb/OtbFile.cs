using System;
using FileFormat.Core;

namespace FileFormat.Otb;

/// <summary>In-memory representation of an OTB (Nokia Over-The-Air Bitmap) image.</summary>
public readonly record struct OtbFile : IImageFormatReader<OtbFile>, IImageToRawImage<OtbFile>, IImageFromRawImage<OtbFile>, IImageFormatWriter<OtbFile> {

  static string IImageFormatMetadata<OtbFile>.PrimaryExtension => ".otb";
  static string[] IImageFormatMetadata<OtbFile>.FileExtensions => [".otb"];
  static OtbFile IImageFormatReader<OtbFile>.FromSpan(ReadOnlySpan<byte> data) => OtbReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<OtbFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<OtbFile>.ToBytes(OtbFile file) => OtbWriter.ToBytes(file);
  /// <summary>Image width in pixels (1..255).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (1..255).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Index 0 is the background and index 1 the ink, so a set bit draws black.
  /// </summary>
  /// <remarks>
  /// The two were the other way round, which turned every image of this format into its own negative:
  /// the bits a writer sets to mark ink were being painted white and the blank background black.
  /// Nothing that only checked an image's size would notice, since a negative is exactly as big as
  /// the picture it inverts.
  /// </remarks>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(OtbFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static OtbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width is < 1 or > 255)
      throw new ArgumentOutOfRangeException(nameof(image), "OTB width must be in the range 1..255.");
    if (image.Height is < 1 or > 255)
      throw new ArgumentOutOfRangeException(nameof(image), "OTB height must be in the range 1..255.");

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
