using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.EverexFax;

/// <summary>In-memory representation of an Everex Fax EFX image.</summary>
public readonly record struct EverexFaxFile : IImageFormatReader<EverexFaxFile>, IImageToRawImage<EverexFaxFile>, IImageFromRawImage<EverexFaxFile>, IImageFormatWriter<EverexFaxFile> {

  static string IImageFormatMetadata<EverexFaxFile>.PrimaryExtension => ".efx";
  static string[] IImageFormatMetadata<EverexFaxFile>.FileExtensions => [".efx", ".ef3"];
  static EverexFaxFile IImageFormatReader<EverexFaxFile>.FromSpan(ReadOnlySpan<byte> data) => EverexFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<EverexFaxFile>.ToBytes(EverexFaxFile file) => EverexFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "EFAX" (0x45 0x46 0x41 0x58).</summary>
  internal static readonly byte[] Magic = [0x45, 0x46, 0x41, 0x58];

  /// <summary>Header size: magic(4) + version(2) + width(2) + height(2) + pages(2) + compression(2) + reserved(2) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Number of pages.</summary>
  public ushort Pages { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public ushort Compression { get; init; }

  /// <summary>Reserved field.</summary>
  public ushort Reserved { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this EFX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(EverexFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static EverexFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A set bit is ink on white paper, the way round ToRawImage reads it back again. The
    // opposite polarity is just as common among scanner formats and would hand back every
    // picture as its own negative.
    return new() {
      Width = image.Width,
      Height = image.Height,
      // One uncompressed page; a RawImage carries no second one.
      Version = 1,
      Pages = 1,
      Compression = 0,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: false),
    };
  }

}
