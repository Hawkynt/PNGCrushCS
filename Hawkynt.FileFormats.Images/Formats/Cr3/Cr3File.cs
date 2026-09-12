using System;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Cr3;

/// <summary>In-memory representation of a Canon CR3 raw file.</summary>
/// <remarks>
/// Camera-authored CR3 files keep their sensor samples in Canon's CRX codec, which is not implemented
/// here. Reading therefore exposes the JPEG preview and thumbnail stored beside that sensor data.
/// Writing deliberately stops at the same boundary: an arbitrary <see cref="RawImage"/> is encoded as
/// a JPEG preview inside a CR3 container, without inventing a CRX track, camera model, lens or exposure.
/// </remarks>
[FormatDetectionPriority(210)]
[FormatMimeType("image/x-canon-cr3")]
// XnView reads what this writes through its `pmp` loader — the "JPEG based file" path — which is
// to say it finds the JPEG preview rather than decoding a CRX track. That is the right check for
// this writer and not a wider one: a preview inside a container is exactly what it builds, so the
// claim means the container is well enough formed for another program to find it, and nothing about
// sensor data this package never writes.
[VerifiedBy(ConformanceOracle.XnView)]
public sealed class Cr3File :
  IImageFormatReader<Cr3File>, IImageToRawImage<Cr3File>, IImageFromRawImage<Cr3File>, IImageFormatWriter<Cr3File> {

  static string IImageFormatMetadata<Cr3File>.PrimaryExtension => ".cr3";
  static string[] IImageFormatMetadata<Cr3File>.FileExtensions => [".cr3"];
  static Cr3File IImageFormatReader<Cr3File>.FromSpan(ReadOnlySpan<byte> data) => Cr3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Cr3File>.ToBytes(Cr3File file) => Cr3Writer.ToBytes(file);

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

  /// <summary>Builds a preview-only CR3 container representation from an arbitrary picture.</summary>
  /// <remarks>
  /// CR3 states preview dimensions as unsigned sixteen-bit values. The picture is JPEG-encoded through
  /// this package's managed JPEG writer; no sensor-data track or camera-authored metadata is synthesised.
  /// </remarks>
  public static Cr3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    _ValidateDimension(image.Width, nameof(image.Width));
    _ValidateDimension(image.Height, nameof(image.Height));

    return new() {
      PreviewJpeg = FormatIO.Encode<JpegFile>(image),
      PreviewWidth = image.Width,
      PreviewHeight = image.Height,
    };
  }

  /// <summary>The largest picture the file carries outside its sensor data.</summary>
  public static RawImage ToRawImage(Cr3File file) {
    ArgumentNullException.ThrowIfNull(file);

    var jpeg = file.PreviewJpeg ?? file.ThumbnailJpeg
               ?? throw new ArgumentException("CR3 carries no preview or thumbnail.", nameof(file));

    return JpegFile.ToRawImage(JpegReader.FromBytes(jpeg));
  }

  private static void _ValidateDimension(int value, string name) {
    if (value is <= 0 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(name, value, $"CR3 preview dimensions must be between 1 and {ushort.MaxValue} pixels.");
  }
}
