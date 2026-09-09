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
  public bool IsIndexed => RawPixelFormats.Get(this.Format).IsIndexed;

  /// <summary>Whether this image stores Y, U/Cb and V/Cr as three tightly packed planes.</summary>
  public bool IsPlanarYuv => RawPixelFormats.Get(this.Format).IsPlanarYuv;

  /// <summary>Whether this image stores IEEE 754 floating-point component samples.</summary>
  public bool IsFloatingPoint => RawPixelFormats.Get(this.Format).IsFloatingPoint;

  /// <summary>Number of physical sample planes in <see cref="PixelData"/>.</summary>
  public int PlaneCount => RawPixelFormats.Get(this.Format).PlaneCount;

  /// <summary>Whether this image has an alpha channel (format-based check with alpha table scan for indexed formats).</summary>
  public bool HasAlpha {
    get {
      var traits = RawPixelFormats.Get(this.Format);
      if (traits.Alpha == RawPixelAlphaKind.Channel)
        return true;

      if (traits.Alpha != RawPixelAlphaKind.Palette || this.AlphaTable is null)
        return false;

      foreach (var alpha in this.AlphaTable)
        if (alpha < byte.MaxValue)
          return true;

      return false;
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
  public static int BytesPerPixel(PixelFormat format) => RawPixelFormats.Get(format).BytesPerPixel;

  /// <summary>
  /// The fewest bytes a picture of this size and format could possibly be held in.
  /// </summary>
  public long MinimumPixelDataLength {
    get {
      var traits = RawPixelFormats.Get(this.Format);
      if (traits.IsPlanarYuv) {
        var chromaWidth = ((long)this.Width + traits.ChromaSubsampleX - 1) / traits.ChromaSubsampleX;
        var chromaHeight = ((long)this.Height + traits.ChromaSubsampleY - 1) / traits.ChromaSubsampleY;
        var bytesPerSample = _BytesPerComponentSample(traits.ComponentBitDepth);
        return ((long)this.Width * this.Height + 2 * chromaWidth * chromaHeight) * bytesPerSample;
      }

      return ((long)this.Width * this.Height * traits.StorageBitsPerPixel + 7) / 8;
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
  public static int BitsPerPixel(PixelFormat format) => RawPixelFormats.Get(format).StorageBitsPerPixel;

  /// <summary>Whether a format is one of the canonical Y/U/V planar layouts.</summary>
  public static bool IsPlanarYuvFormat(PixelFormat format) => RawPixelFormats.Get(format).IsPlanarYuv;

  /// <summary>Whether a format stores IEEE 754 component samples.</summary>
  public static bool IsFloatingPointFormat(PixelFormat format) => RawPixelFormats.Get(format).IsFloatingPoint;

  /// <summary>Effective precision of a YUV component sample.</summary>
  public static int YuvBitDepth(PixelFormat format) => _YuvLayout(format).BitDepth;

  /// <summary>Returns horizontal and vertical chroma subsampling factors for a planar YUV format.</summary>
  public static (int Horizontal, int Vertical) YuvSubsampling(PixelFormat format) {
    var layout = _YuvLayout(format);
    return (layout.SubsampleX, layout.SubsampleY);
  }

  private static (int SubsampleX, int SubsampleY, int BytesPerSample, int BitDepth) _YuvLayout(PixelFormat format) {
    var traits = RawPixelFormats.Get(format);
    if (!traits.IsPlanarYuv)
      throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not planar YUV.");

    return (
      traits.ChromaSubsampleX,
      traits.ChromaSubsampleY,
      _BytesPerComponentSample(traits.ComponentBitDepth),
      traits.ComponentBitDepth
    );
  }

  private static int _BytesPerComponentSample(int bitDepth) => (bitDepth + 7) / 8;
}
