using System;
using FileFormat.Core;

namespace FileFormat.Fpx;

/// <summary>In-memory representation of a FlashPix picture.</summary>
/// <remarks>
/// FlashPix is a Microsoft compound document containing an image-object hierarchy, property sets
/// and one or more tiled resolutions. The writer emits the lossless non-hierarchical form allowed
/// by the 1.0 specification: one full-resolution NIF RGB subimage, still numbered as it would be in
/// a complete pyramid.
/// <para/>
/// The CFB signature is deliberately not registered as FlashPix magic. Word, Excel, PowerPoint and
/// many unrelated compound formats begin with exactly the same eight bytes; identifying all of them
/// as pictures was a format-detector bug. Extension detection selects <c>.fpx</c>/<c>.mix</c>, and the
/// reader then verifies that the compound document actually contains FlashPix image structures.
/// </remarks>
[FormatMimeType("image/vnd.fpx")]
public readonly record struct FpxFile
  : IImageFormatReader<FpxFile>, IImageToRawImage<FpxFile>, IImageFromRawImage<FpxFile>, IImageFormatWriter<FpxFile> {

  static string IImageFormatMetadata<FpxFile>.PrimaryExtension => ".fpx";

  /// <summary><c>.mix</c> is Microsoft Picture It! and PhotoDraw, which store a FlashPix inside.</summary>
  /// <remarks>
  /// Nineteen of the twenty-one <c>.mix</c> samples here are compound files carrying the same Data
  /// Object Store, Resolution and Subimage structure a <c>.fpx</c> does, so they are the same
  /// picture format under another program's name. The other two are neither, and are refused on the
  /// signature.
  /// </remarks>
  static string[] IImageFormatMetadata<FpxFile>.FileExtensions => [".fpx", ".mix"];

  static bool? IImageFormatMetadata<FpxFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < CompoundFile.Signature.Length ? null : CompoundFile.HasSignature(header) ? null : false;

  static FpxFile IImageFormatReader<FpxFile>.FromSpan(ReadOnlySpan<byte> data) => FpxReader.FromSpan(data);
  static byte[] IImageFormatWriter<FpxFile>.ToBytes(FpxFile file) => FpxWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<FpxFile>.VideoModes
    => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])];

  /// <summary>Width of the largest resolution the pyramid holds.</summary>
  public int Width { get; init; }

  /// <summary>Height of the largest resolution the pyramid holds.</summary>
  public int Height { get; init; }

  /// <summary>The assembled tiles, three bytes a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FpxFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData ?? [],
  };

  public static FpxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    return new() {
      Width = rgb.Width,
      Height = rgb.Height,
      PixelData = rgb.PixelData[..],
    };
  }
}
