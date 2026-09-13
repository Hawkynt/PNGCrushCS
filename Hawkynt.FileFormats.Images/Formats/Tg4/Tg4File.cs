using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.Tg4;

/// <summary>In-memory representation of a TG4 image.</summary>
public readonly record struct Tg4File : IImageFormatReader<Tg4File>, IImageToRawImage<Tg4File>, IImageFromRawImage<Tg4File>, IImageFormatWriter<Tg4File> {

  static string IImageFormatMetadata<Tg4File>.PrimaryExtension => ".tg4";
  static string[] IImageFormatMetadata<Tg4File>.FileExtensions => [".tg4"];
  static Tg4File IImageFormatReader<Tg4File>.FromSpan(ReadOnlySpan<byte> data) => Tg4Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Tg4File>.ToBytes(Tg4File file) => Tg4Writer.ToBytes(file);

  /// <summary>Magic bytes: "TG4\0" (0x54 0x47 0x34 0x00).</summary>
  internal static readonly byte[] Magic = [0x54, 0x47, 0x34, 0x00];

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

  /// <summary>Converts this TG4 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Tg4File file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static Tg4File FromRawImage(RawImage image) {
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
