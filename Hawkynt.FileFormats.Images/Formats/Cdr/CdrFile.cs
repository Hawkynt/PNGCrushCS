using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Cdr;

/// <summary>A RIFF-era CorelDRAW document, with its top-level chunks preserved as opaque payloads.</summary>
/// <remarks>
/// CorelDRAW files through the RIFF generation use a <c>CDR?</c> (and, for one version, <c>cdr?</c>)
/// RIFF form. Some of their <c>LIST</c> chunks, notably <c>cmpr</c>, contain Corel-compressed data
/// rather than ordinary RIFF sub-chunks, so parsed files deliberately preserve every top-level chunk
/// byte-for-byte instead of projecting the file onto the generic recursive RIFF object model.
/// <para/>
/// Existing documents can be re-serialized and their <c>DISP</c> thumbnail replaced. Arbitrary raster
/// input is authored as a CorelDRAW 4 document containing one real bitmap object backed by a standard
/// BMP plus a matching <c>DISP</c> preview. This is bitmap-on-page authoring, not vectorization and not
/// an encoder for the later compressed or ZIP-based CorelDRAW generations.
/// </remarks>
[FormatMagicBytes([(byte)'C', (byte)'D', (byte)'R'], 8)]
[FormatMagicBytes([(byte)'c', (byte)'d', (byte)'r'], 8)]
[VerifiedBy(ConformanceOracle.LibreOffice)]
public sealed class CdrFile :
  IImageFormatReader<CdrFile>, IImageToRawImage<CdrFile>, IImageFromRawImage<CdrFile>, IImageFormatWriter<CdrFile> {

  /// <summary>Upper bound used while validating embedded previews and newly-authored bitmap pages.</summary>
  public const int MaxDimension = 20000;

  static string IImageFormatMetadata<CdrFile>.PrimaryExtension => ".cdr";
  static string[] IImageFormatMetadata<CdrFile>.FileExtensions => [".cdr"];
  static CdrFile IImageFormatReader<CdrFile>.FromSpan(ReadOnlySpan<byte> data) => CdrReader.FromSpan(data);
  static CdrFile IImageFromRawImage<CdrFile>.FromRawImage(RawImage image) => CdrWriter.FromRawImage(image);
  static byte[] IImageFormatWriter<CdrFile>.ToBytes(CdrFile file) => CdrWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CdrFile>.VideoModes => [
    new("Bitmap page / embedded preview", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))])
  ];

  /// <summary>The four-byte RIFF form, for example <c>CDR9</c>, <c>CDRD</c> or <c>cdr8</c>.</summary>
  public required FourCC FormType { get; init; }

  /// <summary>Corel's numeric version from <c>vrsn</c>, or the form-derived version when absent.</summary>
  public int Version { get; init; }

  /// <summary>Top-level chunks in original order. <c>LIST</c> payloads remain opaque.</summary>
  public List<RiffChunk> Chunks { get; init; } = [];

  /// <summary>The first valid <c>DISP</c> bitmap preview, when the document has one.</summary>
  public RawImage? Preview { get; init; }

  /// <summary>Bytes following the RIFF extent; preserved by the writer without changing the RIFF size.</summary>
  public byte[] TrailingData { get; init; } = [];

  public static RawImage ToRawImage(CdrFile file)
    => file.Preview ?? throw new InvalidOperationException("The CorelDRAW document has no decodable DISP preview.");
}
