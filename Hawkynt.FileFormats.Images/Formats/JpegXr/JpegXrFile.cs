using System;
using FileFormat.Core;

namespace FileFormat.JpegXr;

/// <summary>In-memory representation of a JPEG XR (ITU-T T.832 / ISO 29199-2) image.</summary>
[VerifiedBy(ConformanceOracle.XnView)]
public readonly record struct JpegXrFile : IImageFormatReader<JpegXrFile>, IImageToRawImage<JpegXrFile>, IImageFromRawImage<JpegXrFile>, IImageFormatWriter<JpegXrFile> {

  static string IImageFormatMetadata<JpegXrFile>.PrimaryExtension => ".jxr";
  static string[] IImageFormatMetadata<JpegXrFile>.FileExtensions => [".jxr", ".wdp", ".hdp"];
  static JpegXrFile IImageFormatReader<JpegXrFile>.FromSpan(ReadOnlySpan<byte> data) => JpegXrReader.FromSpan(data);

  static bool? IImageFormatMetadata<JpegXrFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x01 && header[3] == 0xBC
      ? true : null;

  static byte[] IImageFormatWriter<JpegXrFile>.ToBytes(JpegXrFile file) => JpegXrWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Number of components per pixel that carry meaning: colour channels plus alpha — 1 for grey, 3 for
  /// RGB, 4 for RGBA or CMYK, 5 for CMYKA, and up to 9 for the n-channel formats.
  /// </summary>
  public int ComponentCount { get; init; }

  /// <summary>The raster samples, laid out as <see cref="Format"/> says.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// The layout of <see cref="PixelData"/>. Left unset it is Gray8, RGB24 or RGBA32 for one, three or
  /// four components, which is what a picture built for writing states.
  /// </summary>
  public PixelFormat? Format { get; init; }

  /// <summary>The JPEG XR pixel format the file was coded in, by its WIC name (<c>40bppCMYKAlpha</c> and so on); null for a picture built for writing.</summary>
  public string? SourcePixelFormat { get; init; }

  /// <summary>
  /// Every sample of the file in its own pixel format, rows <see cref="SourceStride"/> bytes apart,
  /// multi-byte samples little-endian — the only place an n-channel picture's channels past the
  /// third, or a CMYK picture's inks, survive as stated.
  /// </summary>
  public byte[]? SourceSamples { get; init; }

  /// <summary>Bytes from one row of <see cref="SourceSamples"/> to the next.</summary>
  public int SourceStride { get; init; }

  public static RawImage ToRawImage(JpegXrFile file) {
    if (file.Format is { } stated)
      return new() { Width = file.Width, Height = file.Height, Format = stated, PixelData = file.PixelData[..] };

    var format = file.ComponentCount switch {
      1 => PixelFormat.Gray8,
      3 => PixelFormat.Rgb24,
      4 => PixelFormat.Rgba32,
      _ => throw new NotSupportedException($"JPEG XR with {file.ComponentCount} components is not supported.")
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..],
    };
  }

  public static JpegXrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    // The writer codes eight-bit grey, RGB and RGBA; a picture goes to whichever of those keeps what
    // it has — its alpha if it carries one, a single channel if it is grey — rather than always to
    // RGBA, which would invent an opaque alpha plane for a picture that never had one.
    image = image.Format switch {
      PixelFormat.Gray8 or PixelFormat.Rgb24 or PixelFormat.Rgba32 => image,
      PixelFormat.Gray16 or PixelFormat.Gray10 or PixelFormat.GrayF16 or PixelFormat.GrayF32 => image.EnsureFormat(PixelFormat.Gray8),
      PixelFormat.Rgb48 or PixelFormat.Rgb565 or PixelFormat.Rgb30 or PixelFormat.Bgr24 or PixelFormat.RgbF16 or PixelFormat.RgbF32 => image.EnsureFormat(PixelFormat.Rgb24),
      _ => image.EnsureFormat(PixelFormat.Rgba32),
    };
    var componentCount = image.Format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.Rgb24 => 3,
      PixelFormat.Rgba32 => 4,
      _ => throw new ArgumentException($"Pixel format {image.Format} is not supported by JPEG XR.", nameof(image))
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      ComponentCount = componentCount,
      PixelData = image.PixelData[..],
    };
  }
}
