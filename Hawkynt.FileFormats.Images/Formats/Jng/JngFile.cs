using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Jng;

/// <summary>In-memory representation of a JNG image.</summary>
[FormatMagicBytes([0x8B, 0x4A, 0x4E, 0x47])]
public readonly record struct JngFile : IImageFormatReader<JngFile>, IImageToRawImage<JngFile>, IImageFromRawImage<JngFile>, IImageFormatWriter<JngFile> {

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

  /// <summary>Decodes the picture, and the alpha beside it when the file carries one.</summary>
  /// <remarks>
  /// A JNG is a JPEG in a PNG's clothing: the chunk structure and the alpha come from PNG, the
  /// picture itself is JPEG. That is the whole point of the format — it exists so a photograph can
  /// have a real alpha channel — so decoding one means decoding a JPEG, which this project already
  /// does. Refusing on that ground refused the format for a dependency it already had.
  /// <para/>
  /// Alpha carried as a greyscale JPEG is decoded the same way; carried as PNG image data it is
  /// left alone, since that path needs the PNG filter chain rather than a second decoder.
  /// </remarks>
  public static RawImage ToRawImage(JngFile file) {
    var jpeg = file.JpegData ?? [];
    if (jpeg.Length == 0)
      throw new InvalidDataException("A JNG carries no JPEG data.");

    var picture = PixelConverter.Convert(
      FileFormat.Jpeg.JpegFile.ToRawImage(FileFormat.Jpeg.JpegReader.FromBytes(jpeg)), PixelFormat.Rgb24);

    if (file.AlphaCompression != JngAlphaCompression.Jpeg || file.AlphaData is not { Length: > 0 } alphaData)
      return picture;

    // The alpha arrives as its own greyscale JPEG of the same size, one sample a pixel.
    var alpha = PixelConverter.Convert(
      FileFormat.Jpeg.JpegFile.ToRawImage(FileFormat.Jpeg.JpegReader.FromBytes(alphaData)), PixelFormat.Rgb24);

    var count = picture.Width * picture.Height;
    var rgba = new byte[count * 4];
    for (var i = 0; i < count; ++i) {
      rgba[i * 4] = picture.PixelData[i * 3];
      rgba[i * 4 + 1] = picture.PixelData[i * 3 + 1];
      rgba[i * 4 + 2] = picture.PixelData[i * 3 + 2];
      rgba[i * 4 + 3] = i * 3 < alpha.PixelData.Length ? alpha.PixelData[i * 3] : (byte)255;
    }

    return new() {
      Width = picture.Width,
      Height = picture.Height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba,
    };
  }

  /// <summary>Creates a JNG from a <see cref="RawImage"/> of any size.</summary>
  /// <remarks>
  /// The colour goes through this library's own JPEG encoder rather than a second one written for
  /// the occasion, so a JNG is exactly a JPEG in a PNG-shaped wrapper — which is what the format is.
  /// Being JPEG, the colour is lossy; only the size and the alpha survive a round trip untouched.
  /// <para/>
  /// An alpha channel becomes a second, greyscale JPEG in a JDAA chunk, mirroring the JDAA branch
  /// <see cref="ToRawImage"/> decodes. The deflate-coded IDAT alternative is not written, because it
  /// is not read either, and a file this library could not open again would be worse than none.
  /// </remarks>
  public static JngFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.EnsureAnyFormat(PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray8);
    var hasAlpha = source.Format == PixelFormat.Rgba32;
    var colour = hasAlpha ? source.EnsureFormat(PixelFormat.Rgb24) : source;
    var isGray = colour.Format == PixelFormat.Gray8;

    byte[]? alphaData = null;
    if (hasAlpha) {
      var pixelCount = source.Width * source.Height;
      var alpha = new byte[pixelCount];
      for (var i = 0; i < pixelCount; ++i)
        alpha[i] = source.PixelData[(i * 4) + 3];

      alphaData = FileFormat.Jpeg.JpegWriter.ToBytes(FileFormat.Jpeg.JpegFile.FromRawImage(new() {
        Width = source.Width,
        Height = source.Height,
        Format = PixelFormat.Gray8,
        PixelData = alpha,
      }));
    }

    return new() {
      Width = source.Width,
      Height = source.Height,
      ColorType = (byte)((isGray ? 8 : 10) + (hasAlpha ? 4 : 0)),
      ImageSampleDepth = 8,
      AlphaSampleDepth = hasAlpha ? (byte)8 : (byte)0,
      AlphaCompression = JngAlphaCompression.Jpeg,
      JpegData = FileFormat.Jpeg.JpegWriter.ToBytes(FileFormat.Jpeg.JpegFile.FromRawImage(colour)),
      AlphaData = alphaData,
    };
  }

}
