using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.TeliFax;

/// <summary>In-memory representation of a TeliFax MH image.</summary>
public readonly record struct TeliFaxFile : IImageFormatReader<TeliFaxFile>, IImageToRawImage<TeliFaxFile>, IImageFromRawImage<TeliFaxFile>, IImageFormatWriter<TeliFaxFile> {

  static string IImageFormatMetadata<TeliFaxFile>.PrimaryExtension => ".mh";
  static string[] IImageFormatMetadata<TeliFaxFile>.FileExtensions => [".mh"];
  static TeliFaxFile IImageFormatReader<TeliFaxFile>.FromSpan(ReadOnlySpan<byte> data) => TeliFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<TeliFaxFile>.ToBytes(TeliFaxFile file) => TeliFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "TF" (0x54 0x46).</summary>
  internal static readonly byte[] Magic = [0x54, 0x46];

  /// <summary>Header size: magic(2) + version(2) + width(2) + height(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this MH image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(TeliFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static TeliFaxFile FromRawImage(RawImage image) {
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
