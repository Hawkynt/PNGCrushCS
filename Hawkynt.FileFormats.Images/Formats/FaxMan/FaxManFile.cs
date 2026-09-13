using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.FaxMan;

/// <summary>In-memory representation of a FaxMan FMF image.</summary>
public readonly record struct FaxManFile : IImageFormatReader<FaxManFile>, IImageToRawImage<FaxManFile>, IImageFromRawImage<FaxManFile>, IImageFormatWriter<FaxManFile> {

  static string IImageFormatMetadata<FaxManFile>.PrimaryExtension => ".fmf";
  static string[] IImageFormatMetadata<FaxManFile>.FileExtensions => [".fmf"];
  static FaxManFile IImageFormatReader<FaxManFile>.FromSpan(ReadOnlySpan<byte> data) => FaxManReader.FromSpan(data);
  static byte[] IImageFormatWriter<FaxManFile>.ToBytes(FaxManFile file) => FaxManWriter.ToBytes(file);

  /// <summary>Magic bytes: "FM" (0x46 0x4D).</summary>
  internal static readonly byte[] Magic = [0x46, 0x4D];

  /// <summary>Header size: magic(2) + width(2) + height(2) + version(2) + flags(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this FMF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FaxManFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static FaxManFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A set bit is ink on white paper, the way round ToRawImage reads it back again. The
    // opposite polarity is just as common among scanner formats and would hand back every
    // picture as its own negative.
    return new() {
      Width = image.Width,
      Height = image.Height,
      Version = 1,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: false),
    };
  }

}
