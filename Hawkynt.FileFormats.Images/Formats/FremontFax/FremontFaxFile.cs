using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.FremontFax;

/// <summary>In-memory representation of a Fremont Fax F96 image.</summary>
public readonly record struct FremontFaxFile : IImageFormatReader<FremontFaxFile>, IImageToRawImage<FremontFaxFile>, IImageFromRawImage<FremontFaxFile>, IImageFormatWriter<FremontFaxFile> {

  static string IImageFormatMetadata<FremontFaxFile>.PrimaryExtension => ".f96";
  static string[] IImageFormatMetadata<FremontFaxFile>.FileExtensions => [".f96"];
  static FremontFaxFile IImageFormatReader<FremontFaxFile>.FromSpan(ReadOnlySpan<byte> data) => FremontFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<FremontFaxFile>.ToBytes(FremontFaxFile file) => FremontFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "F96\0" (0x46 0x39 0x36 0x00).</summary>
  internal static readonly byte[] Magic = [0x46, 0x39, 0x36, 0x00];

  /// <summary>Header size: magic(4) + width(2) + height(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this F96 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(FremontFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static FremontFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A set bit is ink on white paper, the way round ToRawImage reads it back again. The
    // opposite polarity is just as common among scanner formats and would hand back every
    // picture as its own negative.
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: false),
    };
  }

}
