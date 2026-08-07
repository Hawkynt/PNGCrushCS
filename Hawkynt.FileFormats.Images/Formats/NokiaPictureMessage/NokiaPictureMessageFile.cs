using System;
using FileFormat.Core;

namespace FileFormat.NokiaPictureMessage;

/// <summary>In-memory representation of a Nokia Picture Message (.npm) monochrome bitmap image.</summary>
public readonly record struct NokiaPictureMessageFile : IImageFormatReader<NokiaPictureMessageFile>, IImageToRawImage<NokiaPictureMessageFile>, IImageFromRawImage<NokiaPictureMessageFile>, IImageFormatWriter<NokiaPictureMessageFile> {

  static string IImageFormatMetadata<NokiaPictureMessageFile>.PrimaryExtension => ".npm";
  static string[] IImageFormatMetadata<NokiaPictureMessageFile>.FileExtensions => [".npm"];
  static NokiaPictureMessageFile IImageFormatReader<NokiaPictureMessageFile>.FromSpan(ReadOnlySpan<byte> data) => NokiaPictureMessageReader.FromSpan(data);
  /// <summary>
  /// Up to 255 each way: the header holds each dimension in a single byte.
  /// </summary>
  /// <remarks>
  /// This said any size was allowed while the writer threw for anything past 255, so the metadata
  /// promised pictures the format cannot hold.
  /// </remarks>
  static VideoMode[] IImageFormatMetadata<NokiaPictureMessageFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))], [2])
  ];
  static byte[] IImageFormatWriter<NokiaPictureMessageFile>.ToBytes(NokiaPictureMessageFile file) => NokiaPictureMessageWriter.ToBytes(file);

  /// <summary>The largest either dimension can be: the header holds each in one byte.</summary>
  public const int MaxDimension = 255;

  /// <summary>Image width in pixels (1..255).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (1..255).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row, no padding.</summary>
  public byte[] PixelData { get; init; }

  // Nokia convention: 0=white, 1=black
  private static readonly byte[] _WhiteBlackPalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(NokiaPictureMessageFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _WhiteBlackPalette[..],
    PaletteCount = 2,
  };

  public static NokiaPictureMessageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width is < 1 or > MaxDimension)
      throw new ArgumentOutOfRangeException(nameof(image), $"NPM width must be in the range 1..{MaxDimension}.");
    if (image.Height is < 1 or > MaxDimension)
      throw new ArgumentOutOfRangeException(nameof(image), $"NPM height must be in the range 1..{MaxDimension}.");

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
