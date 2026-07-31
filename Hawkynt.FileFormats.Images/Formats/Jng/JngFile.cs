using System;
using FileFormat.Core;
using FileFormat.Jpeg;
using System.IO;

namespace FileFormat.Jng;

/// <summary>In-memory representation of a JNG image.</summary>
[FormatMagicBytes([0x8B, 0x4A, 0x4E, 0x47])]
public readonly record struct JngFile : IImageFormatReader<JngFile>, IImageToRawImage<JngFile>, IImageFormatWriter<JngFile> {

  static string IImageFormatMetadata<JngFile>.PrimaryExtension => ".jng";
  static string[] IImageFormatMetadata<JngFile>.FileExtensions => [".jng"];
  static JngFile IImageFormatReader<JngFile>.FromSpan(ReadOnlySpan<byte> data) => JngReader.FromSpan(data);
  static byte[] IImageFormatWriter<JngFile>.ToBytes(JngFile file) => JngWriter.ToBytes(file);
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
  public byte[] JpegData { get; init; }

  /// <summary>Concatenated alpha channel data from all JDAA or IDAT chunks, or null if no alpha.</summary>
  public byte[]? AlphaData { get; init; }

  /// <summary>Decodes the JNG's JPEG payload, and its alpha channel where it has one.</summary>
  /// <remarks>
  /// This used to refuse outright, saying JPEG decoding "is not available in this library" and
  /// pointing at FileFormat.Jpeg — which is in this same assembly and has been all along. So every
  /// JNG failed for want of a call that was one line away.
  ///
  /// A JNG's colour data is a whole JPEG stream, and its alpha, when present, is either a second
  /// greyscale JPEG (JDAA) or PNG-style deflate (IDAT). The first is decoded here; the second still
  /// is not, and now says so precisely rather than as a blanket refusal.
  /// </remarks>
  public static RawImage ToRawImage(JngFile file) {
    if (file.JpegData is not { Length: > 0 })
      throw new InvalidDataException("JNG carries no JDAT image data.");

    var colour = JpegFile.ToRawImage(JpegReader.FromBytes(file.JpegData)).EnsureFormat(PixelFormat.Rgb24);
    if (file.AlphaData is not { Length: > 0 })
      return colour;

    if (file.AlphaCompression != JngAlphaCompression.Jpeg)
      throw new NotSupportedException(
        "JNG alpha stored as PNG deflate (IDAT) is not supported yet; only a JPEG-coded alpha channel (JDAA) is.");

    var alpha = JpegFile.ToRawImage(JpegReader.FromBytes(file.AlphaData)).EnsureFormat(PixelFormat.Rgb24);
    var pixels = new byte[colour.Width * colour.Height * 4];
    for (var i = 0; i < colour.Width * colour.Height; ++i) {
      pixels[i * 4] = colour.PixelData[i * 3];
      pixels[(i * 4) + 1] = colour.PixelData[(i * 3) + 1];
      pixels[(i * 4) + 2] = colour.PixelData[(i * 3) + 2];

      // The alpha image is greyscale, so any one of its channels is the value.
      var at = i * 3;
      pixels[(i * 4) + 3] = at < alpha.PixelData.Length ? alpha.PixelData[at] : (byte)255;
    }

    return new() {
      Width = colour.Width,
      Height = colour.Height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };
  }

}
