using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.AdTechFax;

/// <summary>In-memory representation of an AdTech fax image.</summary>
public readonly record struct AdTechFaxFile : IImageFormatReader<AdTechFaxFile>, IImageToRawImage<AdTechFaxFile>, IImageFromRawImage<AdTechFaxFile>, IImageFormatWriter<AdTechFaxFile> {

  static string IImageFormatMetadata<AdTechFaxFile>.PrimaryExtension => ".adt";
  static string[] IImageFormatMetadata<AdTechFaxFile>.FileExtensions => [".adt"];
  static AdTechFaxFile IImageFormatReader<AdTechFaxFile>.FromSpan(ReadOnlySpan<byte> data) => AdTechFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<AdTechFaxFile>.ToBytes(AdTechFaxFile file) => AdTechFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "ADTF" (0x41 0x44 0x54 0x46).</summary>
  internal static readonly byte[] Magic = [0x41, 0x44, 0x54, 0x46];

  /// <summary>Header size: magic(4) + width(2) + height(2) + resolution(2) + reserved(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Scan resolution in DPI.</summary>
  public ushort Resolution { get; init; }

  /// <summary>Reserved field.</summary>
  public ushort Reserved { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this ADT image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AdTechFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static AdTechFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A set bit is ink on white paper, the way round ToRawImage reads it back again. The
    // opposite polarity is just as common among scanner formats and would hand back every
    // picture as its own negative.
    return new() {
      Width = image.Width,
      Height = image.Height,
      // 200 dpi: the fine mode a fax scans at, and what these files normally declare.
      Resolution = 200,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: false),
    };
  }

}
