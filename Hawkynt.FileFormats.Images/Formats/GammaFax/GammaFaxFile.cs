using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.GammaFax;

/// <summary>In-memory representation of a GammaFax GMF image.</summary>
public readonly record struct GammaFaxFile : IImageFormatReader<GammaFaxFile>, IImageToRawImage<GammaFaxFile>, IImageFromRawImage<GammaFaxFile>, IImageFormatWriter<GammaFaxFile> {

  static string IImageFormatMetadata<GammaFaxFile>.PrimaryExtension => ".gmf";
  static string[] IImageFormatMetadata<GammaFaxFile>.FileExtensions => [".gmf"];
  static GammaFaxFile IImageFormatReader<GammaFaxFile>.FromSpan(ReadOnlySpan<byte> data) => GammaFaxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GammaFaxFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];
  static byte[] IImageFormatWriter<GammaFaxFile>.ToBytes(GammaFaxFile file) => GammaFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "GF" (0x47 0x46).</summary>
  internal static readonly byte[] Magic = [0x47, 0x46];

  /// <summary>Header size: magic(2) + version(2) + width(2) + height(2) + compression(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public ushort Compression { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this GMF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(GammaFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static GammaFaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A set bit is ink on white paper, the way round ToRawImage reads it back again. The
    // opposite polarity is just as common among scanner formats and would hand back every
    // picture as its own negative.
    return new() {
      Width = image.Width,
      Height = image.Height,
      // Version 1 uncompressed — nothing in the layout varies by version, and the rows go in raw.
      Version = 1,
      Compression = 0,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: false),
    };
  }

}
