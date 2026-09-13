using System;
using FileFormat.BilevelFax;
using FileFormat.Core;

namespace FileFormat.VentaFax;

/// <summary>In-memory representation of a VentaFax VFX image.</summary>
public readonly record struct VentaFaxFile : IImageFormatReader<VentaFaxFile>, IImageToRawImage<VentaFaxFile>, IImageFromRawImage<VentaFaxFile>, IImageFormatWriter<VentaFaxFile> {

  static string IImageFormatMetadata<VentaFaxFile>.PrimaryExtension => ".vfx";
  static string[] IImageFormatMetadata<VentaFaxFile>.FileExtensions => [".vfx"];
  static VentaFaxFile IImageFormatReader<VentaFaxFile>.FromSpan(ReadOnlySpan<byte> data) => VentaFaxReader.FromSpan(data);
  static byte[] IImageFormatWriter<VentaFaxFile>.ToBytes(VentaFaxFile file) => VentaFaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "VFAX" (0x56 0x46 0x41 0x58).</summary>
  internal static readonly byte[] Magic = [0x56, 0x46, 0x41, 0x58];

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

  /// <summary>Converts this VFX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(VentaFaxFile file) => FaxPage.ToRawImage(file.Width, file.Height, file.PixelData);

  /// <summary>Thresholds any <see cref="RawImage"/> down to the two tones this format holds.
  /// Every size fits, because the header states its own.</summary>
  public static VentaFaxFile FromRawImage(RawImage image) {
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
