using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.WorldportFax;

/// <summary>In-memory representation of a WorldportFax WPF image.</summary>
public readonly record struct WorldportFaxFile : IImageFormatReader<WorldportFaxFile>, IImageToRawImage<WorldportFaxFile>, IImageFromRawImage<WorldportFaxFile>, IImageFormatWriter<WorldportFaxFile> {

  static string IImageFormatMetadata<WorldportFaxFile>.PrimaryExtension => ".wpf";
  static string[] IImageFormatMetadata<WorldportFaxFile>.FileExtensions => [".wpf", ".wfx"];
  static WorldportFaxFile IImageFormatReader<WorldportFaxFile>.FromSpan(ReadOnlySpan<byte> data) => WorldportFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<WorldportFaxFile>.ToBytes(WorldportFaxFile file) => WorldportFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "WPFX" (0x57 0x50 0x46 0x58).</summary>
  internal static readonly byte[] Magic = [0x57, 0x50, 0x46, 0x58];

  /// <summary>Header size: magic(4) + width(2) + height(2) + flags(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Flags field.</summary>
  public ushort Flags { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this WPF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(WorldportFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static WorldportFaxFile FromRawImage(RawImage image) {
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
