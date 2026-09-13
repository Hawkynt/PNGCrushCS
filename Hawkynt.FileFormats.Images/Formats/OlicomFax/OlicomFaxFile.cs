using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.OlicomFax;

/// <summary>In-memory representation of an OlicomFax OFX image.</summary>
public readonly record struct OlicomFaxFile : IImageFormatReader<OlicomFaxFile>, IImageToRawImage<OlicomFaxFile>, IImageFromRawImage<OlicomFaxFile>, IImageFormatWriter<OlicomFaxFile> {

  static string IImageFormatMetadata<OlicomFaxFile>.PrimaryExtension => ".ofx";
  static string[] IImageFormatMetadata<OlicomFaxFile>.FileExtensions => [".ofx"];
  static OlicomFaxFile IImageFormatReader<OlicomFaxFile>.FromSpan(ReadOnlySpan<byte> data) => OlicomFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<OlicomFaxFile>.ToBytes(OlicomFaxFile file) => OlicomFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "OLFX" (0x4F 0x4C 0x46 0x58).</summary>
  internal static readonly byte[] Magic = [0x4F, 0x4C, 0x46, 0x58];

  /// <summary>Header size: magic(4) + width(2) + height(2) + flags(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this OFX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(OlicomFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static OlicomFaxFile FromRawImage(RawImage image) {
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
