using System;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Cr3;

/// <summary>In-memory representation of a Canon CR3 raw file.</summary>
/// <remarks>
/// Read and not written. A CR3 is a camera's file: writing one from arbitrary
/// pixels would state a sensor, a lens and an exposure that never happened, which
/// is the same reason the other camera formats here are read-only.
/// <see cref="Cr3Writer"/> builds the container around a preview so the reader
/// can be checked against another tool, and stays off the registry's writer
/// contract, which asks for a picture.
/// </remarks>
[FormatDetectionPriority(210)]
[FormatMimeType("image/x-canon-cr3")]
public sealed class Cr3File : IImageFormatReader<Cr3File>, IImageToRawImage<Cr3File> {

  static string IImageFormatMetadata<Cr3File>.PrimaryExtension => ".cr3";
  static string[] IImageFormatMetadata<Cr3File>.FileExtensions => [".cr3"];
  static Cr3File IImageFormatReader<Cr3File>.FromSpan(ReadOnlySpan<byte> data) => Cr3Reader.FromSpan(data);

  /// <summary>
  /// Recognises a CR3 by the brand its <c>ftyp</c> states.
  /// </summary>
  /// <remarks>
  /// The box structure is shared with MP4 and HEIF, so the box alone says
  /// nothing; the brand is what makes it Canon's.
  /// </remarks>
  static bool? IImageFormatMetadata<Cr3File>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p'
       && header[8] == (byte)'c' && header[9] == (byte)'r' && header[10] == (byte)'x' && header[11] == (byte)' '
      ? true : null;

  /// <summary>The <c>CNCV</c> string, which names the codec the sensor data is in.</summary>
  public string CodecVersion { get; init; } = string.Empty;

  /// <summary>The full-size preview the camera stored, as a complete JPEG.</summary>
  public byte[]? PreviewJpeg { get; init; }

  public int PreviewWidth { get; init; }
  public int PreviewHeight { get; init; }

  /// <summary>The thumbnail, as a complete JPEG.</summary>
  public byte[]? ThumbnailJpeg { get; init; }

  public int ThumbnailWidth { get; init; }
  public int ThumbnailHeight { get; init; }

  /// <summary>The largest picture the file carries outside its sensor data.</summary>
  public static RawImage ToRawImage(Cr3File file) {
    ArgumentNullException.ThrowIfNull(file);

    var jpeg = file.PreviewJpeg ?? file.ThumbnailJpeg
               ?? throw new ArgumentException("CR3 carries no preview or thumbnail.", nameof(file));

    return JpegFile.ToRawImage(JpegReader.FromBytes(jpeg));
  }
}
