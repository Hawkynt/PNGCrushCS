using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.OazFax;

/// <summary>In-memory representation of an OazFax OAZ/XFX image.</summary>
public readonly record struct OazFaxFile : IImageFormatReader<OazFaxFile>, IImageToRawImage<OazFaxFile>, IImageFromRawImage<OazFaxFile>, IImageFormatWriter<OazFaxFile> {

  static string IImageFormatMetadata<OazFaxFile>.PrimaryExtension => ".oaz";
  static string[] IImageFormatMetadata<OazFaxFile>.FileExtensions => [".oaz", ".xfx"];
  static OazFaxFile IImageFormatReader<OazFaxFile>.FromSpan(ReadOnlySpan<byte> data) => OazFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<OazFaxFile>.ToBytes(OazFaxFile file) => OazFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "OAZF" (0x4F 0x41 0x5A 0x46).</summary>
  internal static readonly byte[] Magic = [0x4F, 0x41, 0x5A, 0x46];

  /// <summary>Header size: magic(4) + version(2) + width(2) + height(2) + encoding(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Encoding type (0 = uncompressed).</summary>
  public ushort Encoding { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this OAZ image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(OazFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static OazFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A set bit is ink on white paper, the way round ToRawImage reads it back again. The
    // opposite polarity is just as common among scanner formats and would hand back every
    // picture as its own negative.
    return new() {
      Width = image.Width,
      Height = image.Height,
      // Version 1, encoding 0 — the uncompressed rows this writer emits.
      Version = 1,
      Encoding = 0,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: false),
    };
  }

}
