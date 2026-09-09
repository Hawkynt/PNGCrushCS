using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Riff;

namespace FileFormat.Cdr;

/// <summary>
/// A CorelDRAW drawing stored in the direct RIFF container used through CorelDRAW X3.
/// </summary>
/// <remarks>
/// This deliberately models only the outer RIFF container and the embedded preview. Corel's object
/// data stays opaque: in particular, <c>LIST cmpr</c> is a compressed Corel structure and is not a
/// normal recursively parseable RIFF list. Keeping each top-level chunk verbatim lets a parsed
/// drawing be re-serialized without pretending to understand or regenerate its vector document.
/// </remarks>
[FormatMagicBytes([0x43, 0x44, 0x72], offset: 8)]
[FormatMagicBytes([0x43, 0x44, 0x52], offset: 8)]
[FormatMagicBytes([0x63, 0x64, 0x72], offset: 8)]
public sealed class CdrFile : IImageFormatReader<CdrFile>, IImageToRawImage<CdrFile>, IImageFormatWriter<CdrFile> {

  public static string PrimaryExtension => ".cdr";
  public static string[] FileExtensions => [".cdr"];

  static CdrFile IImageFormatReader<CdrFile>.FromSpan(ReadOnlySpan<byte> data) => CdrReader.FromSpan(data);

  /// <summary>The four-byte RIFF form (for example <c>CDr9</c>).</summary>
  public required FourCC FormType { get; init; }

  /// <summary>The top-level RIFF chunks, in their original order.</summary>
  /// <remarks>
  /// <c>LIST</c> is intentionally kept as a chunk whose data begins with its four-byte list type.
  /// This preserves Corel's compressed <c>LIST cmpr</c> payload instead of feeding it to a generic
  /// RIFF list parser that would interpret compressed bytes as child chunk headers.
  /// </remarks>
  public IReadOnlyList<RiffChunk> Chunks { get; init; } = [];

  /// <summary>The version from the two-byte <c>vrsn</c> chunk, when present.</summary>
  public ushort? Version { get; init; }

  /// <summary>The decoded <c>DISP</c> thumbnail, when the drawing carries one.</summary>
  public RawImage? Preview { get; init; }

  /// <summary>A replacement requested by <see cref="WithPreview"/>; parsed previews remain raw on disk.</summary>
  internal RawImage? ReplacementPreview { get; init; }

  /// <summary>Whether the header describes a direct RIFF-based CorelDRAW drawing.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < RiffHeader.StructSize)
      return null;

    if (!header[..4].SequenceEqual("RIFF"u8))
      return null;

    return _IsCdrForm(FourCC.ReadFrom(header[8..12])) ? true : null;
  }

  /// <summary>Reads a CorelDRAW drawing from a file.</summary>
  public static CdrFile FromFile(FileInfo file) => CdrReader.FromFile(file);

  /// <summary>Reads a CorelDRAW drawing from bytes.</summary>
  public static CdrFile FromBytes(byte[] data) => CdrReader.FromBytes(data);

  /// <summary>Reads a CorelDRAW drawing from a stream.</summary>
  public static CdrFile FromStream(Stream stream) => CdrReader.FromStream(stream);

  /// <summary>Serializes a parsed direct-RIFF CorelDRAW drawing.</summary>
  public static byte[] ToBytes(CdrFile file) => CdrWriter.ToBytes(file);

  /// <summary>Returns the drawing's embedded preview.</summary>
  public static RawImage ToRawImage(CdrFile file)
    => file.Preview ?? throw new InvalidOperationException("The CDR file does not contain a decodable DISP preview.");

  /// <summary>
  /// Returns a copy that writes <paramref name="preview"/> into the existing <c>DISP</c> chunk.
  /// </summary>
  /// <remarks>
  /// This does not invent a drawing or a new Corel chunk graph. The existing four-byte DISP prefix,
  /// all non-preview chunks and their order are preserved; only the packed DIB after that prefix is
  /// replaced.
  /// </remarks>
  public CdrFile WithPreview(RawImage preview) {
    ArgumentNullException.ThrowIfNull(preview);

    foreach (var chunk in this.Chunks)
      if (chunk.Id.ToString() == "DISP") {
        if (chunk.Data.Length < sizeof(uint))
          throw new InvalidOperationException("The existing DISP chunk is too short to hold its Corel prefix.");

        return new() {
          FormType = this.FormType,
          Chunks = this.Chunks,
          Version = this.Version,
          Preview = preview,
          ReplacementPreview = preview,
        };
      }

    throw new InvalidOperationException("A preview can only be replaced when the CDR file already contains a DISP chunk.");
  }

  internal static bool IsDirectRiffCdrForm(FourCC formType) => _IsCdrForm(formType);

  private static bool _IsCdrForm(FourCC formType) {
    static byte _Lower(byte value) => value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;

    // Corel files occur as CDr#, CDR# and cdr# in independent format descriptions and samples.
    // CDRX is the related CCX format, not a CDR drawing.
    return _Lower(formType.A) == (byte)'c'
      && _Lower(formType.B) == (byte)'d'
      && _Lower(formType.C) == (byte)'r'
      && _Lower(formType.D) != (byte)'x';
  }

}
