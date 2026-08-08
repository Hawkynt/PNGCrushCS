using System;
using FileFormat.Core;

namespace FileFormat.AdexImage;

/// <summary>In-memory representation of an ADEX image.</summary>
public readonly record struct AdexImageFile : IImageFormatReader<AdexImageFile>, IImageToRawImage<AdexImageFile>, IImageFromRawImage<AdexImageFile>, IImageFormatWriter<AdexImageFile> {

  static string IImageFormatMetadata<AdexImageFile>.PrimaryExtension => ".adx";
  static string[] IImageFormatMetadata<AdexImageFile>.FileExtensions => [".adx"];
  static AdexImageFile IImageFormatReader<AdexImageFile>.FromSpan(ReadOnlySpan<byte> data) => AdexImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<AdexImageFile>.ToBytes(AdexImageFile file) => AdexImageWriter.ToBytes(file);

  /// <summary>Magic bytes: "ADEX" (0x41 0x44 0x45 0x58).</summary>
  internal static readonly byte[] Magic = [0x41, 0x44, 0x45, 0x58];

  /// <summary>Header size: magic(4) + width(2) + height(2) + bpp(2) + compression(2) = 12 bytes.</summary>
  internal const int HeaderSize = 12;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>The largest either dimension goes, which is what the header's words hold.</summary>
  public const int MaxDimension = 65535;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Compression type.</summary>
  public ushort Compression { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this ADEX image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AdexImageFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates an ADEX image from a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// The header carries the dimensions, so any size fits; only the pixel layout is fixed, and the
  /// reader hands the bytes straight out as three per pixel.
  /// </remarks>
  public static AdexImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The header states the size as words, and a picture bigger than one holds would be written
    // with its dimensions wrapped — a file that reads back as a different picture rather than as a
    // broken one, which is the worse of the two failures.
    if (image.Width is < 1 or > MaxDimension || image.Height is < 1 or > MaxDimension)
      throw new ArgumentException(
        $"An ADEX image is at most {MaxDimension}x{MaxDimension}; got {image.Width}x{image.Height}.", nameof(image));

    var source = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = source.Width,
      Height = source.Height,
      Bpp = 24,
      Compression = 0,
      PixelData = source.PixelData[..],
    };
  }

}
