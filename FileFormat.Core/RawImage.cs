using System;

namespace FileFormat.Core;

/// <summary>Platform-independent pixel buffer that serves as the intermediate type for cross-format image conversion.</summary>
public sealed class RawImage {

  /// <summary>The width of the image in pixels.</summary>
  public required int Width { get; init; }

  /// <summary>The height of the image in pixels.</summary>
  public required int Height { get; init; }

  /// <summary>The pixel format describing how bytes in <see cref="PixelData"/> are laid out.</summary>
  public required PixelFormat Format { get; init; }

  /// <summary>The raw pixel data in the layout described by <see cref="Format"/>.</summary>
  public required byte[] PixelData { get; init; }

  /// <summary>
  /// Optional interpretation of component values as colour: range, primaries, transfer, matrix and
  /// chroma location. Particularly important for planar YUV and HDR formats, where layout alone is
  /// insufficient to reconstruct the intended colours.
  /// </summary>
  public RawImageColorInfo? ColorInfo { get; init; }

  /// <summary>Optional palette entries as RGB triplets (3 bytes per entry). Required for indexed pixel formats.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>The number of entries in the palette (0 when no palette is present).</summary>
  public int PaletteCount { get; init; }

  /// <summary>Optional per-palette-entry alpha values. Used for PNG tRNS-style transparency on indexed images.</summary>
  public byte[]? AlphaTable { get; init; }

  /// <summary>Optional EXIF/XMP/IPTC/ICC/DPI/text metadata carried alongside the pixels. <c>null</c>
  /// means "the source format had none" or "the reader doesn't extract it" — never treat null as
  /// license to fabricate a substitute.</summary>
  public ImageMetadata? Metadata { get; init; }

  /// <summary>Whether this image uses an indexed pixel format.</summary>
  public bool IsIndexed => Format is PixelFormat.Indexed8 or PixelFormat.Indexed4 or PixelFormat.Indexed1 or PixelFormat.Indexed16;

  /// <summary>Whether this image stores Y, U/Cb and V/Cr as three tightly packed planes.</summary>
  public bool IsPlanarYuv => IsPlanarYuvFormat(this.Format);

  /// <summary>Whether this image stores IEEE 754 floating-point component samples.</summary>
  public bool IsFloatingPoint => IsFloatingPointFormat(this.Format);

  /// <summary>Number of physical sample planes in <see cref="PixelData"/>.</summary>
  public int PlaneCount => this.IsPlanarYuv ? 3 : 1;

  /// <summary>Whether this image has an alpha channel (format-based check with alpha table scan for indexed formats).</summary>
  public bool HasAlpha {
    get {
      switch (Format) {
        case PixelFormat.Bgra32:
        case PixelFormat.Rgba32:
        case PixelFormat.Argb32:
        case PixelFormat.Rgba64:
        case PixelFormat.GrayAlpha16:
        case PixelFormat.GrayAlpha32:
        case PixelFormat.GrayAlphaF16:
        case PixelFormat.RgbaF16:
        case PixelFormat.GrayAlphaF32:
        case PixelFormat.RgbaF32:
          return true;
        case PixelFormat.Indexed8:
        case PixelFormat.Indexed4:
        case PixelFormat.Indexed1:
        case PixelFormat.Indexed16:
          if (AlphaTable == null)
            return false;
          foreach (var a in AlphaTable)
            if (a < 255)
              return true;
          return false;
        default:
          return false;
      }
    }
  }

  /// <summary>Converts this image to BGRA32 pixel data through the accelerated shared conversion pipeline.</summary>
  public byte[] ToBgra32() => Format == PixelFormat.Bgra32 ? PixelData : FastRawImageConverter.Convert(this, PixelFormat.Bgra32).PixelData;

  /// <summary>Converts this image to RGBA32 pixel data through the accelerated shared conversion pipeline.</summary>
  public byte[] ToRgba32() => Format == PixelFormat.Rgba32 ? PixelData : FastRawImageConverter.Convert(this, PixelFormat.Rgba32).PixelData;

  /// <summary>Converts this image to RGB24 pixel data through the accelerated shared conversion pipeline.</summary>
  public byte[] ToRgb24() => Format == PixelFormat.Rgb24 ? PixelData : FastRawImageConverter.Convert(this, PixelFormat.Rgb24).PixelData;

  /// <summary>
  /// Returns one plane's dimensions. Packed formats have one plane equal to the image size; planar
  /// YUV has a full-resolution Y plane followed by chroma planes rounded up at the subsampling edge.
  /// </summary>
  public (int Width, int Height) GetPlaneDimensions(int plane) {
    if ((uint)plane >= (uint)this.PlaneCount)
      throw new ArgumentOutOfRangeException(nameof(plane));

    if (!this.IsPlanarYuv || plane == 0)
      return (this.Width, this.Height);

    var (subsampleX, subsampleY, _, _) = _YuvLayout(this.Format);
    return ((this.Width + subsampleX - 1) / subsampleX, (this.Height + subsampleY - 1) / subsampleY);
  }

  /// <summary>Returns the byte offset at which a physical plane begins.</summary>
  public int GetPlaneOffset(int plane) {
    if ((uint)plane >= (uint)this.PlaneCount)
      throw new ArgumentOutOfRangeException(nameof(plane));

    if (plane == 0)
      return 0;

    var (_, _, bytesPerSample, _) = _YuvLayout(this.Format);
    var yBytes = checked(this.Width * this.Height * bytesPerSample);
    if (plane == 1)
      return yBytes;

    var (chromaWidth, chromaHeight) = this.GetPlaneDimensions(1);
    return checked(yBytes + chromaWidth * chromaHeight * bytesPerSample);
  }

  /// <summary>Returns the number of bytes occupied by a physical plane.</summary>
  public int GetPlaneLength(int plane) {
    var (width, height) = this.GetPlaneDimensions(plane);
    var bytesPerSample = this.IsPlanarYuv ? _YuvLayout(this.Format).BytesPerSample : BytesPerPixel(this.Format);
    return checked(width * height * bytesPerSample);
  }

  /// <summary>Returns a read-only view over one physical plane.</summary>
  public ReadOnlySpan<byte> GetPlaneData(int plane) {
    var offset = this.GetPlaneOffset(plane);
    var length = this.GetPlaneLength(plane);
    if (offset < 0 || length < 0 || offset > this.PixelData.Length - length)
      throw new InvalidOperationException(
        $"The {this.Format} buffer is too short to contain plane {plane}: it has {this.PixelData.Length} byte(s), "
        + $"but the plane needs bytes {offset} through {offset + length - 1}.");

    return this.PixelData.AsSpan(offset, length);
  }

  /// <summary>Computes the number of bytes per pixel for packed formats, or 0 for non-packed formats.</summary>
  public static int BytesPerPixel(PixelFormat format) => format switch {
    PixelFormat.Bgra32 => 4,
    PixelFormat.Rgba32 => 4,
    PixelFormat.Argb32 => 4,
    PixelFormat.Rgb24 => 3,
    PixelFormat.Bgr24 => 3,
    PixelFormat.Gray8 => 1,
    PixelFormat.Gray16 => 2,
    PixelFormat.GrayAlpha16 => 2,
    PixelFormat.GrayAlpha32 => 4,
    PixelFormat.Indexed8 => 1,
    PixelFormat.Indexed4 => 0,
    PixelFormat.Indexed1 => 0,
    PixelFormat.Indexed16 => 2,
    PixelFormat.Rgba64 => 8,
    PixelFormat.Rgb48 => 6,
    PixelFormat.Rgb565 => 2,
    PixelFormat.Gray10 => 2,
    PixelFormat.Rgb30 => 4,
    PixelFormat.GrayF16 => 2,
    PixelFormat.GrayAlphaF16 => 4,
    PixelFormat.RgbF16 => 6,
    PixelFormat.RgbaF16 => 8,
    PixelFormat.GrayF32 => 4,
    PixelFormat.GrayAlphaF32 => 8,
    PixelFormat.RgbF32 => 12,
    PixelFormat.RgbaF32 => 16,
    PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8
      or PixelFormat.Yuv420P10 or PixelFormat.Yuv422P10 or PixelFormat.Yuv440P10 or PixelFormat.Yuv444P10
      or PixelFormat.Yuv420P12 or PixelFormat.Yuv422P12 or PixelFormat.Yuv440P12 or PixelFormat.Yuv444P12
      or PixelFormat.Yuv420P16 or PixelFormat.Yuv422P16 or PixelFormat.Yuv440P16 or PixelFormat.Yuv444P16 => 0,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
  };

  /// <summary>
  /// The fewest bytes a picture of this size and format could possibly be held in.
  /// </summary>
  public long MinimumPixelDataLength {
    get {
      if (this.IsPlanarYuv) {
        var (subsampleX, subsampleY, bytesPerSample, _) = _YuvLayout(this.Format);
        var chromaWidth = ((long)this.Width + subsampleX - 1) / subsampleX;
        var chromaHeight = ((long)this.Height + subsampleY - 1) / subsampleY;
        return ((long)this.Width * this.Height + 2 * chromaWidth * chromaHeight) * bytesPerSample;
      }

      return ((long)this.Width * this.Height * BitsPerPixel(this.Format) + 7) / 8;
    }
  }

  /// <summary>Whether the picture carries enough samples to fill the size it states.</summary>
  public bool HasEnoughPixelData {
    get {
      if (this.Width <= 0 || this.Height <= 0)
        return false;

      try {
        return this.PixelData != null && this.PixelData.LongLength >= this.MinimumPixelDataLength;
      } catch (ArgumentOutOfRangeException) {
        return true;
      }
    }
  }

  /// <summary>Computes the stored number of bits per pixel for fixed-rate formats.</summary>
  public static int BitsPerPixel(PixelFormat format) => format switch {
    PixelFormat.Bgra32 => 32,
    PixelFormat.Rgba32 => 32,
    PixelFormat.Argb32 => 32,
    PixelFormat.Rgb24 => 24,
    PixelFormat.Bgr24 => 24,
    PixelFormat.Gray8 => 8,
    PixelFormat.Gray16 => 16,
    PixelFormat.GrayAlpha16 => 16,
    PixelFormat.GrayAlpha32 => 32,
    PixelFormat.Indexed8 => 8,
    PixelFormat.Indexed4 => 4,
    PixelFormat.Indexed1 => 1,
    PixelFormat.Indexed16 => 16,
    PixelFormat.Rgba64 => 64,
    PixelFormat.Rgb48 => 48,
    PixelFormat.Rgb565 => 16,
    PixelFormat.Gray10 => 16,
    PixelFormat.Rgb30 => 32,
    PixelFormat.GrayF16 => 16,
    PixelFormat.GrayAlphaF16 => 32,
    PixelFormat.RgbF16 => 48,
    PixelFormat.RgbaF16 => 64,
    PixelFormat.GrayF32 => 32,
    PixelFormat.GrayAlphaF32 => 64,
    PixelFormat.RgbF32 => 96,
    PixelFormat.RgbaF32 => 128,
    PixelFormat.Yuv420P8 => 12,
    PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 => 16,
    PixelFormat.Yuv444P8 => 24,
    PixelFormat.Yuv420P10 or PixelFormat.Yuv420P12 or PixelFormat.Yuv420P16 => 24,
    PixelFormat.Yuv422P10 or PixelFormat.Yuv440P10 or PixelFormat.Yuv422P12 or PixelFormat.Yuv440P12
      or PixelFormat.Yuv422P16 or PixelFormat.Yuv440P16 => 32,
    PixelFormat.Yuv444P10 or PixelFormat.Yuv444P12 or PixelFormat.Yuv444P16 => 48,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
  };

  /// <summary>Whether a format is one of the canonical Y/U/V planar layouts.</summary>
  public static bool IsPlanarYuvFormat(PixelFormat format) => format is
    PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8
    or PixelFormat.Yuv420P10 or PixelFormat.Yuv422P10 or PixelFormat.Yuv440P10 or PixelFormat.Yuv444P10
    or PixelFormat.Yuv420P12 or PixelFormat.Yuv422P12 or PixelFormat.Yuv440P12 or PixelFormat.Yuv444P12
    or PixelFormat.Yuv420P16 or PixelFormat.Yuv422P16 or PixelFormat.Yuv440P16 or PixelFormat.Yuv444P16;

  /// <summary>Whether a format stores IEEE 754 component samples.</summary>
  public static bool IsFloatingPointFormat(PixelFormat format) => format is
    PixelFormat.GrayF16 or PixelFormat.GrayAlphaF16 or PixelFormat.RgbF16 or PixelFormat.RgbaF16
    or PixelFormat.GrayF32 or PixelFormat.GrayAlphaF32 or PixelFormat.RgbF32 or PixelFormat.RgbaF32;

  /// <summary>Effective precision of a YUV component sample.</summary>
  public static int YuvBitDepth(PixelFormat format) => _YuvLayout(format).BitDepth;

  /// <summary>Returns horizontal and vertical chroma subsampling factors for a planar YUV format.</summary>
  public static (int Horizontal, int Vertical) YuvSubsampling(PixelFormat format) {
    var layout = _YuvLayout(format);
    return (layout.SubsampleX, layout.SubsampleY);
  }

  private static (int SubsampleX, int SubsampleY, int BytesPerSample, int BitDepth) _YuvLayout(PixelFormat format) => format switch {
    PixelFormat.Yuv420P8 => (2, 2, 1, 8),
    PixelFormat.Yuv422P8 => (2, 1, 1, 8),
    PixelFormat.Yuv440P8 => (1, 2, 1, 8),
    PixelFormat.Yuv444P8 => (1, 1, 1, 8),
    PixelFormat.Yuv420P10 => (2, 2, 2, 10),
    PixelFormat.Yuv422P10 => (2, 1, 2, 10),
    PixelFormat.Yuv440P10 => (1, 2, 2, 10),
    PixelFormat.Yuv444P10 => (1, 1, 2, 10),
    PixelFormat.Yuv420P12 => (2, 2, 2, 12),
    PixelFormat.Yuv422P12 => (2, 1, 2, 12),
    PixelFormat.Yuv440P12 => (1, 2, 2, 12),
    PixelFormat.Yuv444P12 => (1, 1, 2, 12),
    PixelFormat.Yuv420P16 => (2, 2, 2, 16),
    PixelFormat.Yuv422P16 => (2, 1, 2, 16),
    PixelFormat.Yuv440P16 => (1, 2, 2, 16),
    PixelFormat.Yuv444P16 => (1, 1, 2, 16),
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not planar YUV."),
  };
}