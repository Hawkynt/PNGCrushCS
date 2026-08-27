using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Heif;

/// <summary>One independently addressable image item in a HEIF file.</summary>
public readonly record struct HeifImage {
  public uint ItemId { get; init; }
  public string ItemType { get; init; }
  public bool IsPrimary { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[] RawImageData { get; init; }
}

/// <summary>In-memory representation of HEIF/HEIC (ISO/IEC 23008-12).</summary>
/// <remarks>
/// Directly coded HEVC image items are resolved through iinf/iloc/ipma and decoded with the managed
/// H.265 implementation shared with the video package. The primary item is exposed through the
/// ordinary single-image contract, while every directly coded top-level image is available through
/// <see cref="IMultiImageFileFormat{TSelf}"/>.
/// <para/>
/// The writer beside this type remains unregistered: it predates the HEVC encoder and does not emit a
/// conforming HEIF image item. Read support must not be used as evidence that this format is writable.
/// </remarks>
[FormatMimeType("image/heif")]
public readonly record struct HeifFile :
  IImageFormatReader<HeifFile>,
  IImageToRawImage<HeifFile>,
  IImageInfoReader<HeifFile>,
  IMultiImageFileFormat<HeifFile> {

  static string IImageFormatMetadata<HeifFile>.PrimaryExtension => ".heic";
  static string[] IImageFormatMetadata<HeifFile>.FileExtensions => [".heic", ".heif"];
  static FormatCapability IImageFormatMetadata<HeifFile>.Capabilities => FormatCapability.MultiImage;
  static HeifFile IImageFormatReader<HeifFile>.FromSpan(ReadOnlySpan<byte> data) => HeifReader.FromSpan(data);

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) => HeifReader.ReadImageInfo(header);

  static bool? IImageFormatMetadata<HeifFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12
        || header[4] != (byte)'f'
        || header[5] != (byte)'t'
        || header[6] != (byte)'y'
        || header[7] != (byte)'p')
      return null;

    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'c')
      return true;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'i' && header[11] == (byte)'x')
      return true;
    if (header[8] == (byte)'h' && header[9] == (byte)'e' && header[10] == (byte)'v' && header[11] == (byte)'c')
      return true;
    if (header[8] == (byte)'m' && header[9] == (byte)'i' && header[10] == (byte)'f' && header[11] == (byte)'1')
      return true;
    return null;
  }

  /// <summary>The primary image width, after its clean-aperture crop.</summary>
  public int Width { get; init; }

  /// <summary>The primary image height, after its clean-aperture crop.</summary>
  public int Height { get; init; }

  /// <summary>The primary image pixels in Rgb24.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The major brand from ftyp.</summary>
  public string Brand { get; init; }

  /// <summary>The primary item's coded payload, after iloc extent assembly.</summary>
  public byte[] RawImageData { get; init; }

  /// <summary>
  /// Directly coded top-level image items, with the primary item first. Thumbnail and auxiliary
  /// items are deliberately not counted as pages.
  /// </summary>
  public IReadOnlyList<HeifImage> Images { get; init; }

  public static int ImageCount(HeifFile file)
    => file.Images?.Count is > 0 ? file.Images.Count : 1;

  public static RawImage ToRawImage(HeifFile file, int index) {
    var count = ImageCount(file);
    if ((uint)index >= (uint)count)
      throw new ArgumentOutOfRangeException(nameof(index), index, $"The HEIF file contains {count} image(s).");

    if (file.Images?.Count is > 0) {
      var image = file.Images[index];
      return new() {
        Width = image.Width,
        Height = image.Height,
        Format = PixelFormat.Rgb24,
        PixelData = image.PixelData[..],
      };
    }

    return ToRawImage(file);
  }

  public static IReadOnlyList<RawImage> ToRawImages(HeifFile file) {
    var count = ImageCount(file);
    var result = new RawImage[count];
    for (var i = 0; i < count; ++i)
      result[i] = ToRawImage(file, i);
    return result;
  }

  public static RawImage ToRawImage(HeifFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static HeifFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    var pixelData = image.PixelData[..];
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixelData,
      RawImageData = pixelData[..],
      Brand = "heic",
      Images = [],
    };
  }
}
