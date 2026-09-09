using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Cdr;

/// <summary>A RIFF-era CorelDRAW document, with its top-level chunks preserved as opaque payloads.</summary>
/// <remarks>
/// CorelDRAW files through the RIFF generation use a <c>CDR?</c> RIFF form. Some of their <c>LIST</c>
/// chunks, notably <c>cmpr</c>, contain Corel-compressed data rather than ordinary RIFF sub-chunks,
/// so this type deliberately preserves every top-level chunk byte-for-byte instead of projecting
/// the file onto the generic recursive RIFF object model.
/// <para/>
/// This type has a whole-file writer so a parsed document can be re-serialized and its <c>DISP</c>
/// thumbnail can be replaced. It deliberately does not implement <see cref="IImageFromRawImage{TSelf}"/>:
/// a bitmap is not enough information to manufacture a CorelDRAW vector document.
/// </remarks>
[FormatMagicBytes([(byte)'C', (byte)'D', (byte)'R'], 8)]
public sealed class CdrFile : IImageFormatReader<CdrFile>, IImageToRawImage<CdrFile>, IImageFormatWriter<CdrFile> {

  /// <summary>Upper bound used while validating embedded preview dimensions.</summary>
  public const int MaxDimension = 20000;

  static string IImageFormatMetadata<CdrFile>.PrimaryExtension => ".cdr";
  static string[] IImageFormatMetadata<CdrFile>.FileExtensions => [".cdr"];
  static CdrFile IImageFormatReader<CdrFile>.FromSpan(ReadOnlySpan<byte> data) => CdrReader.FromSpan(data);
  static byte[] IImageFormatWriter<CdrFile>.ToBytes(CdrFile file) => CdrWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CdrFile>.VideoModes => [
    new("Embedded preview", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))])
  ];

  /// <summary>The four-byte RIFF form, for example <c>CDR9</c> or <c>CDRD</c>.</summary>
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
