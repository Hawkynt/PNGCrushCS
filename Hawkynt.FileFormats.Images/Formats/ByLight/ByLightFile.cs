using System;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.ByLight;

/// <summary>In-memory representation of a byLight image (.bif).</summary>
/// <remarks>
/// Not a raster format of its own: a fixed 374 byte record followed by one complete JPEG stream.
/// XnView's reader checks the two signature bytes, steps over the remaining 372 bytes without
/// reading any of them, and hands the rest of the file to its JPEG loader; feeding it anything but
/// a JPEG at that offset is refused. That was confirmed against its converter, which reports the
/// embedded image's own size and depth and decodes it as JPEG.
/// <para/>
/// Because the reader never looks inside the record, nothing in it can be recovered from the reader,
/// and the record is kept here verbatim. The vendor's manual describes the format as multi-page, but
/// only the one image at offset 374 is reachable — the converter reports a single page.
/// </remarks>
public readonly record struct ByLightFile : IImageFormatReader<ByLightFile>, IImageToRawImage<ByLightFile>, IImageFromRawImage<ByLightFile>, IImageFormatWriter<ByLightFile> {

  static string IImageFormatMetadata<ByLightFile>.PrimaryExtension => ".bif";
  static string[] IImageFormatMetadata<ByLightFile>.FileExtensions => [".bif"];
  static ByLightFile IImageFormatReader<ByLightFile>.FromSpan(ReadOnlySpan<byte> data) => ByLightReader.FromSpan(data);
  static byte[] IImageFormatWriter<ByLightFile>.ToBytes(ByLightFile file) => ByLightWriter.ToBytes(file);

  /// <summary>Magic bytes at offset 0: 0xFA 0xBA.</summary>
  internal static readonly byte[] Magic = [0xFA, 0xBA];

  /// <summary>The opaque record in front of the JPEG stream is a fixed 374 bytes.</summary>
  internal const int HeaderSize = 374;

  /// <summary>Minimum valid file size: the record plus the shortest conceivable JPEG.</summary>
  public const int MinFileSize = HeaderSize + 2;

  /// <summary>The 374 byte record the reader steps over, kept verbatim.</summary>
  public byte[] Header { get; init; }

  /// <summary>The embedded JPEG stream, byte for byte as it sits in the file.</summary>
  public byte[] JpegData { get; init; }

  /// <summary>Converts this byLight image to a platform-independent <see cref="RawImage"/> by decoding the embedded JPEG.</summary>
  public static RawImage ToRawImage(ByLightFile file) => JpegFile.ToRawImage(JpegReader.FromBytes(file.JpegData));

  /// <summary>Creates a byLight file by placing a conventional JPEG behind the fixed record.</summary>
  public static ByLightFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(image));
    var header = new byte[HeaderSize];
    header[0] = Magic[0];
    header[1] = Magic[1];
    return new() { Header = header, JpegData = jpeg };
  }

}