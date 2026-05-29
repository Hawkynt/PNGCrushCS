using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.BigTiff;

/// <summary>In-memory representation of a BigTIFF (.btf/.tf8) image file.</summary>
public sealed class BigTiffFile : IImageFormatReader<BigTiffFile>, IImageToRawImage<BigTiffFile>, IImageFromRawImage<BigTiffFile>, IImageFormatWriter<BigTiffFile>, IMultiImageFileFormat<BigTiffFile> {

  /// <summary>BigTIFF version number (43).</summary>
  public const ushort Version = 43;

  /// <summary>BigTIFF offset size in bytes (always 8).</summary>
  public const ushort OffsetSize = 8;

  /// <summary>Minimum valid file size: 16-byte header.</summary>
  public const int MinimumFileSize = 16;

  /// <summary>TIFF tag for ImageWidth.</summary>
  internal const ushort TagImageWidth = 256;

  /// <summary>TIFF tag for ImageLength (height).</summary>
  internal const ushort TagImageLength = 257;

  /// <summary>TIFF tag for BitsPerSample.</summary>
  internal const ushort TagBitsPerSample = 258;

  /// <summary>TIFF tag for Compression.</summary>
  internal const ushort TagCompression = 259;

  /// <summary>TIFF tag for PhotometricInterpretation.</summary>
  internal const ushort TagPhotometricInterpretation = 262;

  /// <summary>TIFF tag for StripOffsets.</summary>
  internal const ushort TagStripOffsets = 273;

  /// <summary>TIFF tag for SamplesPerPixel.</summary>
  internal const ushort TagSamplesPerPixel = 277;

  /// <summary>TIFF tag for RowsPerStrip.</summary>
  internal const ushort TagRowsPerStrip = 278;

  /// <summary>TIFF tag for StripByteCounts.</summary>
  internal const ushort TagStripByteCounts = 279;

  /// <summary>TIFF type SHORT (uint16).</summary>
  internal const ushort TypeShort = 3;

  /// <summary>TIFF type LONG (uint32).</summary>
  internal const ushort TypeLong = 4;

  /// <summary>TIFF type LONG8 (uint64).</summary>
  internal const ushort TypeLong8 = 16;

  /// <summary>Compression: none.</summary>
  internal const ushort CompressionNone = 1;

  /// <summary>PhotometricInterpretation: min-is-black (grayscale).</summary>
  internal const ushort PhotometricMinIsBlack = 1;

  /// <summary>PhotometricInterpretation: RGB.</summary>
  internal const ushort PhotometricRgb = 2;

  static string IImageFormatMetadata<BigTiffFile>.PrimaryExtension => ".btf";
  static string[] IImageFormatMetadata<BigTiffFile>.FileExtensions => [".btf", ".tf8"];
  static BigTiffFile IImageFormatReader<BigTiffFile>.FromSpan(ReadOnlySpan<byte> data) => BigTiffReader.FromSpan(data);

  static bool? IImageFormatMetadata<BigTiffFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    if (header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2B && header[3] == 0x00)
      return true;
    if (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2B)
      return true;
    return null;
  }

  static byte[] IImageFormatWriter<BigTiffFile>.ToBytes(BigTiffFile file) => BigTiffWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Samples per pixel (1 for Gray8, 3 for Rgb24).</summary>
  public int SamplesPerPixel { get; init; } = 1;

  /// <summary>Bits per sample (always 8).</summary>
  public int BitsPerSample { get; init; } = 8;

  /// <summary>Photometric interpretation (1=MinIsBlack/Gray, 2=RGB).</summary>
  public ushort PhotometricInterpretation { get; init; } = PhotometricMinIsBlack;

  /// <summary>Raw pixel data (row-major, no padding).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Whether the source file used big-endian byte order.</summary>
  public bool IsBigEndian { get; init; }

  /// <summary>Additional pages beyond the first IFD. Empty for single-page BigTIFFs.</summary>
  public IReadOnlyList<BigTiffPage> Pages { get; init; } = [];

  /// <summary>Returns the total number of pages (IFDs) in the BigTIFF file.</summary>
  public static int ImageCount(BigTiffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return 1 + file.Pages.Count;
  }

  /// <summary>Converts a specific page at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(BigTiffFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    var total = 1 + file.Pages.Count;
    if ((uint)index >= (uint)total)
      throw new ArgumentOutOfRangeException(nameof(index));

    if (index == 0)
      return ToRawImage(file);

    var page = file.Pages[index - 1];
    var is16Bit = page.BitsPerSample > 8;
    var format = page.SamplesPerPixel >= 3
      ? (is16Bit ? PixelFormat.Rgb48 : PixelFormat.Rgb24)
      : (is16Bit ? PixelFormat.Gray16 : PixelFormat.Gray8);
    var bytesPerSample = is16Bit ? 2 : 1;
    var bytesPerPixel = (page.SamplesPerPixel >= 3 ? 3 : 1) * bytesPerSample;
    var expectedSize = page.Width * page.Height * bytesPerPixel;
    var pixelData = new byte[expectedSize];
    page.PixelData.AsSpan(0, Math.Min(page.PixelData.Length, expectedSize)).CopyTo(pixelData);

    return new() {
      Width = page.Width,
      Height = page.Height,
      Format = format,
      PixelData = pixelData,
    };
  }

  public static RawImage ToRawImage(BigTiffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var is16Bit = file.BitsPerSample > 8;
    var format = file.SamplesPerPixel >= 3
      ? (is16Bit ? PixelFormat.Rgb48 : PixelFormat.Rgb24)
      : (is16Bit ? PixelFormat.Gray16 : PixelFormat.Gray8);
    var bytesPerSample = is16Bit ? 2 : 1;
    var bytesPerPixel = (file.SamplesPerPixel >= 3 ? 3 : 1) * bytesPerSample;
    var expectedSize = file.Width * file.Height * bytesPerPixel;
    var pixelData = new byte[expectedSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = pixelData,
    };
  }

  public static BigTiffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    int samplesPerPixel;
    int bitsPerSample;
    ushort photometric;
    switch (image.Format) {
      case PixelFormat.Gray8:
        samplesPerPixel = 1;
        bitsPerSample = 8;
        photometric = PhotometricMinIsBlack;
        break;
      case PixelFormat.Rgb24:
        samplesPerPixel = 3;
        bitsPerSample = 8;
        photometric = PhotometricRgb;
        break;
      case PixelFormat.Gray16:
        samplesPerPixel = 1;
        bitsPerSample = 16;
        photometric = PhotometricMinIsBlack;
        break;
      case PixelFormat.Rgb48:
        samplesPerPixel = 3;
        bitsPerSample = 16;
        photometric = PhotometricRgb;
        break;
      default:
        throw new ArgumentException($"Expected {PixelFormat.Gray8}, {PixelFormat.Rgb24}, {PixelFormat.Gray16}, or {PixelFormat.Rgb48} but got {image.Format}.", nameof(image));
    }

    var bytesPerSample = bitsPerSample > 8 ? 2 : 1;
    var expectedSize = image.Width * image.Height * samplesPerPixel * bytesPerSample;
    var pixelData = new byte[expectedSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, expectedSize)).CopyTo(pixelData);

    return new() {
      Width = image.Width,
      Height = image.Height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PhotometricInterpretation = photometric,
      PixelData = pixelData,
    };
  }
}
