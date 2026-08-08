using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Hpi;

/// <summary>In-memory representation of a Hemera Photo-Object (.hpi).</summary>
/// <remarks>
/// A signature that is PNG's with "HPI" in place of "PNG", then a table of little-endian offsets, and
/// then two pictures of the same size: a JPEG, and a palette-indexed PNG after it. XnView draws the
/// JPEG, so that is what is read here; what the PNG beside it is for — a mask, or an overlay — is not
/// established, and none of the three samples settles it.
/// </remarks>
public readonly record struct HpiFile
  : IImageFormatReader<HpiFile>, IImageToRawImage<HpiFile>,
    IImageFromRawImage<HpiFile>, IImageFormatWriter<HpiFile> {

  static string IImageFormatMetadata<HpiFile>.PrimaryExtension => ".hpi";
  static string[] IImageFormatMetadata<HpiFile>.FileExtensions => [".hpi"];
  static HpiFile IImageFormatReader<HpiFile>.FromSpan(ReadOnlySpan<byte> data) => HpiReader.FromSpan(data);
  static byte[] IImageFormatWriter<HpiFile>.ToBytes(HpiFile file) => HpiWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HpiFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<HpiFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic) ? true : null;

  /// <summary>The eight bytes a file opens with: PNG's signature with HPI in place of PNG.</summary>
  internal static ReadOnlySpan<byte> Magic => [0x89, (byte)'H', (byte)'P', (byte)'I', 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>Where the offset of the JPEG is stated, as a little-endian long word.</summary>
  internal const int JpegOffsetField = 12;

  /// <summary>The head this writes: the signature and the table that states where the picture is.</summary>
  internal const int DefaultJpegOffset = JpegOffsetField + 4;

  /// <summary>The JPEG the file carries, exactly as it stands in it.</summary>
  public byte[] Embedded { get; init; }

  public static RawImage ToRawImage(HpiFile file)
    => JpegFile.ToRawImage(JpegReader.FromBytes(file.Embedded ?? throw new InvalidDataException("A Hemera photo-object carries no picture.")));

  /// <summary>Builds one carrying only the JPEG.</summary>
  /// <remarks>
  /// A real photo-object has a second picture after the first — a palette-indexed PNG whose purpose
  /// none of the samples settles. Writing one whose meaning is unknown would be inventing it, so
  /// only the picture that is understood is written, and the table states where it is.
  /// </remarks>
  public static HpiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() { Embedded = JpegWriter.ToBytes(JpegFile.FromRawImage(image)) };
  }
}
