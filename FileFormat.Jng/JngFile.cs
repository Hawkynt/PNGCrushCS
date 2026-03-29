using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Jng;

/// <summary>In-memory representation of a JNG image.</summary>
[FormatMagicBytes([0x8B, 0x4A, 0x4E, 0x47])]
public sealed class JngFile : IImageFileFormat<JngFile> {

  static string IImageFileFormat<JngFile>.PrimaryExtension => ".jng";
  static string[] IImageFileFormat<JngFile>.FileExtensions => [".jng"];
  static JngFile IImageFileFormat<JngFile>.FromFile(FileInfo file) => JngReader.FromFile(file);
  static JngFile IImageFileFormat<JngFile>.FromBytes(byte[] data) => JngReader.FromBytes(data);
  static JngFile IImageFileFormat<JngFile>.FromStream(Stream stream) => JngReader.FromStream(stream);
  static byte[] IImageFileFormat<JngFile>.ToBytes(JngFile file) => JngWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Color type (8=gray, 10=color, 12=gray+alpha, 14=color+alpha).</summary>
  public byte ColorType { get; init; }

  /// <summary>Image sample depth (8 or 12).</summary>
  public byte ImageSampleDepth { get; init; }

  /// <summary>Alpha sample depth (0 if no alpha, otherwise 1/2/4/8/16).</summary>
  public byte AlphaSampleDepth { get; init; }

  /// <summary>Alpha compression method.</summary>
  public JngAlphaCompression AlphaCompression { get; init; }

  /// <summary>Concatenated JPEG image data from all JDAT chunks.</summary>
  public byte[] JpegData { get; init; } = [];

  /// <summary>Concatenated alpha channel data from all JDAA or IDAT chunks, or null if no alpha.</summary>
  public byte[]? AlphaData { get; init; }

  public static RawImage ToRawImage(JngFile file) {
    ArgumentNullException.ThrowIfNull(file);
    throw new NotSupportedException("JNG conversion requires JPEG decoding which is not available in this library. Use FileFormat.Jpeg to decode JpegData first.");
  }

  public static JngFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("JNG conversion requires JPEG encoding which is not available in this library. Use FileFormat.Jpeg to encode pixel data first.");
  }
}
