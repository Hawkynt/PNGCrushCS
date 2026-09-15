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
  /// signature. Reading that embedded picture does not make <c>.mix</c> a writable FlashPix alias:
  /// writing one would require composing the surrounding Picture It!/PhotoDraw document as well.
  /// </remarks>
  static string[] IImageFormatMetadata<FpxFile>.FileExtensions => [".fpx", ".mix"];

  /// <summary>
  /// Claims a compound file, declines anything else, and answers "undecided" only when there is not
  /// yet enough header to tell.
  /// </summary>
  /// <remarks>
  /// A FlashPix picture has no magic of its own: it is an OLE compound document, and what makes it
  /// FlashPix lives in a stream inside it rather than in the first bytes. Within a registry of image
  /// formats that is enough to name it, because it is the only image format built on a compound
  /// document — a compound file that is a spreadsheet is not something an image registry classifies
  /// either way. Answering "undecided" instead left the format unreachable by signature: the registry
  /// falls through to a magic-byte list, and there is no such list to fall through to.
  /// </remarks>
  static bool? IImageFormatMetadata<FpxFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < CompoundFile.Signature.Length ? null : CompoundFile.HasSignature(header);

  static FpxFile IImageFormatReader<FpxFile>.FromSpan(ReadOnlySpan<byte> data) => FpxReader.FromSpan(data);
  static FpxFile IImageFromRawImage<FpxFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
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

  /// <summary>Creates a FlashPix image only for the extension the writer actually implements.</summary>
  public static FpxFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);
    if (!string.Equals(extension, ".fpx", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException(
        $"FlashPix writing requires the .fpx extension; '{extension}' is only supported for reading.",
        nameof(extension));

    return FromRawImage(image);
  }
}
