using System;
using FileFormat.Core;

namespace FileFormat.Cmx;

/// <summary>A Corel Presentation Exchange (CMX) document represented by its embedded raster preview.</summary>
/// <remarks>
/// CMX 6 and later is externally specified as a RIFF/RIFX container with form <c>CMX1</c>. The
/// document itself is vector/metafile data, so this reader exposes the bitmap preview without
/// pretending that the preview is the document. A raster image is therefore not sufficient to
/// author a CMX file and no <see cref="IImageFromRawImage{TSelf}"/> contract is implemented.
/// </remarks>
[FormatMagicBytes([(byte)'C', (byte)'M', (byte)'X', (byte)'1'], 8)]
public sealed class CmxFile : IImageFormatReader<CmxFile>, IImageToRawImage<CmxFile> {

  static string IImageFormatMetadata<CmxFile>.PrimaryExtension => ".cmx";
  static string[] IImageFormatMetadata<CmxFile>.FileExtensions => [".cmx"];
  static CmxFile IImageFormatReader<CmxFile>.FromSpan(ReadOnlySpan<byte> data) => CmxReader.FromSpan(data);
  static bool? IImageFormatMetadata<CmxFile>.MatchesSignature(ReadOnlySpan<byte> header) => CmxReader.MatchesSignature(header);
  static VideoMode[] IImageFormatMetadata<CmxFile>.VideoModes => [
    new("Embedded preview", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  /// <summary>Whether the outer CMX container is the big-endian <c>RIFX</c> form.</summary>
  public bool IsBigEndian { get; init; }

  /// <summary>The coordinate precision stated by the CMX header: 16 or 32 bits.</summary>
  public int CoordinatePrecisionBits { get; init; }

  /// <summary>The bitmap preview found inside the validated CMX container.</summary>
  public required RawImage Preview { get; init; }

  /// <summary>Byte offset of the packed DIB preview inside the CMX file.</summary>
  public int PreviewOffset { get; init; }

  public static RawImage ToRawImage(CmxFile file)
    => file.Preview ?? throw new InvalidOperationException("The CMX document has no decoded preview.");
}
