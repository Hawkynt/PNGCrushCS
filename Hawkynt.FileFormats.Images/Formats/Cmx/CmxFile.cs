using System;
using FileFormat.Core;

namespace FileFormat.Cmx;

/// <summary>A Corel Presentation Exchange (CMX) document represented by its embedded raster preview.</summary>
/// <remarks>
/// CMX is a RIFF/RIFX metafile whose page can contain both vector primitives and bitmaps. Reading
/// exposes the standard <c>DISP</c> thumbnail stated by the container header; it does not pretend to
/// rasterize arbitrary vector CMX scenes. Writing does author a real CMX page: arbitrary pixels are
/// converted to ordinary filled rectangle primitives and a full-resolution <c>DISP</c> thumbnail is
/// included for preview-oriented readers.
/// </remarks>
[FormatMagicBytes([(byte)'C', (byte)'M', (byte)'X', (byte)'1'], 8)]
public sealed class CmxFile :
  IImageFormatReader<CmxFile>, IImageToRawImage<CmxFile>, IImageFromRawImage<CmxFile>, IImageFormatWriter<CmxFile> {

  static string IImageFormatMetadata<CmxFile>.PrimaryExtension => ".cmx";
  static string[] IImageFormatMetadata<CmxFile>.FileExtensions => [".cmx"];
  static CmxFile IImageFormatReader<CmxFile>.FromSpan(ReadOnlySpan<byte> data) => CmxReader.FromSpan(data);
  static byte[] IImageFormatWriter<CmxFile>.ToBytes(CmxFile file) => CmxWriter.ToBytes(file);
  static bool? IImageFormatMetadata<CmxFile>.MatchesSignature(ReadOnlySpan<byte> header) => CmxReader.MatchesSignature(header);
  static VideoMode[] IImageFormatMetadata<CmxFile>.VideoModes => [
    new("CMX page / embedded preview", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  /// <summary>Whether the source container used Motorola-style big-endian RIFX framing.</summary>
  public bool IsBigEndian { get; init; }

  /// <summary>The coordinate precision declared by the CMX <c>cont</c> header.</summary>
  public int CoordinatePrecisionBits { get; init; }

  /// <summary>The CMX internal format generation from the four-byte major-version field.</summary>
  public int InternalVersion { get; init; }

  /// <summary>The standard <c>DISP</c> thumbnail decoded to raw pixels.</summary>
  public required RawImage Preview { get; init; }

  /// <summary>Absolute file offset at which the packed DIB inside <c>DISP</c> begins.</summary>
  public int PreviewOffset { get; init; }

  public static RawImage ToRawImage(CmxFile file)
    => file.Preview ?? throw new InvalidOperationException("The CMX document has no decoded preview.");

  public static CmxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (!image.HasEnoughPixelData)
      throw new ArgumentException("The source image has invalid dimensions or too little pixel data.", nameof(image));

    var rgba = image.EnsureFormat(PixelFormat.Rgba32);
    return new() {
      IsBigEndian = false,
      CoordinatePrecisionBits = 16,
      InternalVersion = 1,
      Preview = new RawImage {
        Width = rgba.Width,
        Height = rgba.Height,
        Format = PixelFormat.Rgba32,
        PixelData = rgba.PixelData[..],
        Metadata = rgba.Metadata,
      },
      PreviewOffset = 0,
    };
  }
}
