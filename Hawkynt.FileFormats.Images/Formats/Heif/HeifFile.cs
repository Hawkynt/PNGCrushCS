using System;
using System.Buffers.Binary;
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
/// Directly coded HEVC and AVC image items are resolved through iinf/iloc/ipma and decoded with the
/// managed H.265/H.264 implementations shared with the video package. HEIF <c>grid</c> and
/// <c>iden</c> derived images are resolved through their <c>dimg</c> references, including the
/// normative clean-aperture, rotation and mirror transforms. The primary item is exposed through the
/// ordinary single-image contract, while every independent top-level image is available through
/// <see cref="IMultiImageFileFormat{TSelf}"/>.
/// <para/>
/// Writing emits one <c>hvc1</c> Main-Still-Picture item. The first encoder profile deliberately uses
/// HEVC's normative PCM coding-unit mode: the output is large but lossless at the YUV sample level
/// and is a real HEVC bitstream rather than a raw-RGB payload in an HEIF-shaped box tree.
/// </remarks>
[FormatMimeType("image/heic", "image/heif", "image/avci", "image/avcs")]
[VerifiedBy(ConformanceOracle.ImageMagick, ConformanceOracle.HeifDec, ConformanceOracle.FFmpeg)]
public readonly record struct HeifFile :
  IImageFormatReader<HeifFile>,
  IImageToRawImage<HeifFile>,
  IImageFromRawImage<HeifFile>,
  IImageFormatWriter<HeifFile>,
  IImageInfoReader<HeifFile>,
  IMultiImageFileFormat<HeifFile> {

  static string IImageFormatMetadata<HeifFile>.PrimaryExtension => ".heic";
  /// <summary>
  /// The names this container comes under. <c>.avci</c> is the same container
  /// with an H.264 picture in it rather than an H.265 one, which is a different
  /// codec inside the same boxes and not a different format.
  /// </summary>
  static string[] IImageFormatMetadata<HeifFile>.FileExtensions => [".heic", ".heif", ".hif", ".avci", ".avcs"];
  static FormatCapability IImageFormatMetadata<HeifFile>.Capabilities => FormatCapability.MultiImage;
  static HeifFile IImageFormatReader<HeifFile>.FromSpan(ReadOnlySpan<byte> data) => HeifReader.FromSpan(data);
  static byte[] IImageFormatWriter<HeifFile>.ToBytes(HeifFile file) => HeifWriter.ToBytes(file);

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) => HeifReader.ReadImageInfo(header);

  static bool? IImageFormatMetadata<HeifFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12 || !header.Slice(4, 4).SequenceEqual("ftyp"u8))
      return null;

    var size32 = BinaryPrimitives.ReadUInt32BigEndian(header);
    var boxHeaderSize = 8;
    ulong boxSize = size32;
    if (size32 == 1) {
      if (header.Length < 20)
        return null;
      boxSize = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
      boxHeaderSize = 16;
    } else if (size32 == 0) {
      boxSize = (ulong)header.Length;
    }

    if (boxSize < (ulong)(boxHeaderSize + 8))
      return null;

    var availableEnd = (int)Math.Min((ulong)header.Length, boxSize);
    var majorOffset = boxHeaderSize;
    if (majorOffset + 4 > availableEnd)
      return null;

    var hasGenericHeifBrand = false;
    var hasSpecificHeifBrand = false;
    var hasAvifBrand = false;

    void InspectBrand(ReadOnlySpan<byte> brand) {
      if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8)) {
        hasAvifBrand = true;
        return;
      }

      if (brand.SequenceEqual("mif1"u8)) {
        hasGenericHeifBrand = true;
        return;
      }

      if (brand.SequenceEqual("heic"u8)
          || brand.SequenceEqual("heix"u8)
          || brand.SequenceEqual("hevc"u8)
          || brand.SequenceEqual("heim"u8)
          || brand.SequenceEqual("heis"u8)
          || brand.SequenceEqual("hevm"u8)
          || brand.SequenceEqual("hevs"u8)
          || brand.SequenceEqual("avci"u8)
          || brand.SequenceEqual("avcs"u8))
        hasSpecificHeifBrand = true;
    }

    InspectBrand(header.Slice(majorOffset, 4));
    for (var at = majorOffset + 8; at + 4 <= availableEnd; at += 4)
      InspectBrand(header.Slice(at, 4));

    // AVIF is itself a HEIF-conformant format and commonly carries mif1 as a compatible brand.
    // The dedicated AVIF format must win whenever avif/avis is explicitly advertised.
    if (hasAvifBrand)
      return null;

    return hasSpecificHeifBrand || hasGenericHeifBrand ? true : null;
  }

  /// <summary>The primary image width, after its clean-aperture crop and orientation transforms.</summary>
  public int Width { get; init; }

  /// <summary>The primary image height, after its clean-aperture crop and orientation transforms.</summary>
  public int Height { get; init; }

  /// <summary>The primary image pixels in Rgb24.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The major brand from ftyp.</summary>
  public string Brand { get; init; }

  /// <summary>The primary item's coded or derived-item payload, after iloc extent assembly.</summary>
  public byte[] RawImageData { get; init; }

  /// <summary>
  /// Independent top-level image items, with the primary item first. Thumbnail, auxiliary and
  /// component images used only as inputs to a derived image are deliberately not counted as pages.
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
      RawImageData = [],
      Brand = "heic",
      Images = [],
    };
  }
}
