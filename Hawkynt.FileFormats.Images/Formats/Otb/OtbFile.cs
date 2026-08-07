using System;
using FileFormat.Core;

namespace FileFormat.Otb;

/// <summary>In-memory representation of an OTB (Nokia Over-The-Air Bitmap) image.</summary>
public readonly record struct OtbFile : IImageFormatReader<OtbFile>, IImageToRawImage<OtbFile>, IImageFromRawImage<OtbFile>, IImageFormatWriter<OtbFile> {

  static string IImageFormatMetadata<OtbFile>.PrimaryExtension => ".otb";
  static string[] IImageFormatMetadata<OtbFile>.FileExtensions => [".otb"];
  static OtbFile IImageFormatReader<OtbFile>.FromSpan(ReadOnlySpan<byte> data) => OtbReader.FromSpan(data);
  /// <summary>
  /// Up to 255 each way: the header holds each dimension in a single byte.
  /// </summary>
  /// <remarks>
  /// This said any size was allowed while the writer threw for anything past 255, so the metadata
  /// promised pictures the format cannot hold.
  /// </remarks>
  static VideoMode[] IImageFormatMetadata<OtbFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))], [2])
  ];

  /// <summary>The largest either dimension can be: the header holds each in one byte.</summary>
  public const int MaxDimension = 255;
  static byte[] IImageFormatWriter<OtbFile>.ToBytes(OtbFile file) => OtbWriter.ToBytes(file);
  /// <summary>Image width in pixels (1..255).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (1..255).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Index zero is paper, index one is ink.</summary>
  /// <remarks>
  /// A set bit means a dot was drawn, not that the pixel is lit. Reading it the other way gives a
  /// negative of the picture, which round-trips through our own writer perfectly well.
  /// </remarks>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(OtbFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData, file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static OtbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > MaxDimension)
      throw new ArgumentOutOfRangeException(nameof(image), $"OTB width must be in the range 1..{MaxDimension}.");
    if (image.Height is < 1 or > MaxDimension)
      throw new ArgumentOutOfRangeException(nameof(image), $"OTB height must be in the range 1..{MaxDimension}.");

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = BilevelRows.Pack(BilevelRows.Threshold(image, setWhenDark: true), image.Width, image.Height),
    };
  }
}
