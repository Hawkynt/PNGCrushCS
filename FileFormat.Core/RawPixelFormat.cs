using System;

namespace FileFormat.Core;

/// <summary>Broad storage/interpretation family of a typed raw pixel representation.</summary>
public enum RawPixelFormatKind {
  PackedInteger,
  Indexed,
  FloatingPoint,
  PlanarYuv,
}

/// <summary>How alpha, when supported, is represented by a raw pixel format.</summary>
public enum RawPixelAlphaKind {
  None,
  Channel,
  Palette,
}

/// <summary>
/// Static facts about one raw pixel representation. These facts describe the semantic representation
/// and its current in-memory storage; they do not describe file-format-specific packing.
/// </summary>
/// <param name="LegacyFormat">Closest representation in the legacy runtime <see cref="PixelFormat"/> model.</param>
/// <param name="Kind">Broad storage/interpretation family.</param>
/// <param name="StorageBitsPerPixel">Physical in-memory storage bits per pixel used by the compatibility representation.</param>
/// <param name="BytesPerPixel">Whole bytes per packed pixel, or zero for sub-byte/planar layouts.</param>
/// <param name="ComponentBitDepth">Nominal component precision where one value describes every component; zero for mixed layouts.</param>
/// <param name="Alpha">How alpha can be represented.</param>
/// <param name="PlaneCount">Number of physical sample planes.</param>
/// <param name="IndexBitDepth">Logical index width for indexed formats, otherwise zero.</param>
/// <param name="ChromaSubsampleX">Horizontal chroma subsampling factor for planar YUV.</param>
/// <param name="ChromaSubsampleY">Vertical chroma subsampling factor for planar YUV.</param>
public readonly record struct RawPixelFormatTraits(
  PixelFormat LegacyFormat,
  RawPixelFormatKind Kind,
  int StorageBitsPerPixel,
  int BytesPerPixel,
  int ComponentBitDepth,
  RawPixelAlphaKind Alpha = RawPixelAlphaKind.None,
  int PlaneCount = 1,
  int IndexBitDepth = 0,
  int ChromaSubsampleX = 1,
  int ChromaSubsampleY = 1
) {
  public bool IsIndexed => this.Kind == RawPixelFormatKind.Indexed;
  public bool IsFloatingPoint => this.Kind == RawPixelFormatKind.FloatingPoint;
  public bool IsPlanarYuv => this.Kind == RawPixelFormatKind.PlanarYuv;
  public bool CanRepresentAlpha => this.Alpha != RawPixelAlphaKind.None;

  /// <summary>Maximum number of distinct palette entries addressable by this indexed representation.</summary>
  public int MaximumPaletteEntries => this.IndexBitDepth == 0 ? 0 : 1 << this.IndexBitDepth;
}

/// <summary>
/// Compile-time descriptor for a raw pixel representation. The generic parameter deliberately describes
/// representation semantics rather than requiring one CLR value of <typeparamref name="TSelf"/> per pixel.
/// </summary>
public interface IRawPixelFormat<TSelf> where TSelf : IRawPixelFormat<TSelf> {
  static abstract RawPixelFormatTraits Traits { get; }
}

/// <summary>Central compatibility map between the legacy runtime enum and typed raw-pixel traits.</summary>
public static class RawPixelFormats {

  public static RawPixelFormatTraits Get(PixelFormat format) => format switch {
    PixelFormat.Bgra32 => _Packed(format, 32, 4, 8, RawPixelAlphaKind.Channel),
    PixelFormat.Rgba32 => _Packed(format, 32, 4, 8, RawPixelAlphaKind.Channel),
    PixelFormat.Argb32 => _Packed(format, 32, 4, 8, RawPixelAlphaKind.Channel),
    PixelFormat.Rgb24 => _Packed(format, 24, 3, 8),
    PixelFormat.Bgr24 => _Packed(format, 24, 3, 8),
    PixelFormat.Gray8 => _Packed(format, 8, 1, 8),
    PixelFormat.Gray16 => _Packed(format, 16, 2, 16),
    PixelFormat.GrayAlpha16 => _Packed(format, 16, 2, 8, RawPixelAlphaKind.Channel),
    PixelFormat.GrayAlpha32 => _Packed(format, 32, 4, 16, RawPixelAlphaKind.Channel),
    PixelFormat.Indexed8 => _Indexed(8, PixelFormat.Indexed8, 8, 1),
    PixelFormat.Indexed4 => _Indexed(4, PixelFormat.Indexed4, 4, 0),
    PixelFormat.Indexed1 => _Indexed(1, PixelFormat.Indexed1, 1, 0),
    PixelFormat.Indexed16 => _Indexed(16, PixelFormat.Indexed16, 16, 2),
    PixelFormat.Rgba64 => _Packed(format, 64, 8, 16, RawPixelAlphaKind.Channel),
    PixelFormat.Rgb48 => _Packed(format, 48, 6, 16),
    PixelFormat.Rgb565 => _Packed(format, 16, 2, 0),
    PixelFormat.Gray10 => _Packed(format, 16, 2, 10),
    PixelFormat.Rgb30 => _Packed(format, 32, 4, 10),
    PixelFormat.GrayF16 => _Floating(format, 16, 2, 16),
    PixelFormat.GrayAlphaF16 => _Floating(format, 32, 4, 16, RawPixelAlphaKind.Channel),
    PixelFormat.RgbF16 => _Floating(format, 48, 6, 16),
    PixelFormat.RgbaF16 => _Floating(format, 64, 8, 16, RawPixelAlphaKind.Channel),
    PixelFormat.GrayF32 => _Floating(format, 32, 4, 32),
    PixelFormat.GrayAlphaF32 => _Floating(format, 64, 8, 32, RawPixelAlphaKind.Channel),
    PixelFormat.RgbF32 => _Floating(format, 96, 12, 32),
    PixelFormat.RgbaF32 => _Floating(format, 128, 16, 32, RawPixelAlphaKind.Channel),
    PixelFormat.Yuv420P8 => _Yuv(format, 12, 8, 2, 2),
    PixelFormat.Yuv422P8 => _Yuv(format, 16, 8, 2, 1),
    PixelFormat.Yuv440P8 => _Yuv(format, 16, 8, 1, 2),
    PixelFormat.Yuv444P8 => _Yuv(format, 24, 8, 1, 1),
    PixelFormat.Yuv420P10 => _Yuv(format, 24, 10, 2, 2),
    PixelFormat.Yuv422P10 => _Yuv(format, 32, 10, 2, 1),
    PixelFormat.Yuv440P10 => _Yuv(format, 32, 10, 1, 2),
    PixelFormat.Yuv444P10 => _Yuv(format, 48, 10, 1, 1),
    PixelFormat.Yuv420P12 => _Yuv(format, 24, 12, 2, 2),
    PixelFormat.Yuv422P12 => _Yuv(format, 32, 12, 2, 1),
    PixelFormat.Yuv440P12 => _Yuv(format, 32, 12, 1, 2),
    PixelFormat.Yuv444P12 => _Yuv(format, 48, 12, 1, 1),
    PixelFormat.Yuv420P16 => _Yuv(format, 24, 16, 2, 2),
    PixelFormat.Yuv422P16 => _Yuv(format, 32, 16, 2, 1),
    PixelFormat.Yuv440P16 => _Yuv(format, 32, 16, 1, 2),
    PixelFormat.Yuv444P16 => _Yuv(format, 48, 16, 1, 1),
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
  };

  /// <summary>
  /// Describes a logical indexed representation from one through sixteen bits without extending the
  /// legacy enum. Widths that have no legacy packed form use the next lossless whole-byte storage type:
  /// 2/3/5/6/7-bit indices use <see cref="PixelFormat.Indexed8"/>, and 9-15-bit indices use
  /// <see cref="PixelFormat.Indexed16"/>.
  /// </summary>
  public static RawPixelFormatTraits Indexed(int bitDepth) => bitDepth switch {
    1 => _Indexed(1, PixelFormat.Indexed1, 1, 0),
    4 => _Indexed(4, PixelFormat.Indexed4, 4, 0),
    8 => _Indexed(8, PixelFormat.Indexed8, 8, 1),
    16 => _Indexed(16, PixelFormat.Indexed16, 16, 2),
    >= 2 and <= 7 => _Indexed(bitDepth, PixelFormat.Indexed8, 8, 1),
    >= 9 and <= 15 => _Indexed(bitDepth, PixelFormat.Indexed16, 16, 2),
    _ => throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "Indexed pixel formats support 1 through 16 bits."),
  };

  /// <summary>
  /// Checks value-level invariants that are stricter than the compatibility storage type. This is
  /// currently needed by logical indexed widths such as Indexed6 that are stored as legacy Indexed8.
  /// </summary>
  public static void ValidateDeclaredRepresentation(RawImage image, RawPixelFormatTraits traits) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format != traits.LegacyFormat)
      throw new ArgumentException($"Expected compatibility format {traits.LegacyFormat}, got {image.Format}.", nameof(image));

    if (!image.HasEnoughPixelData)
      throw new ArgumentException("The raw image does not contain enough pixel data for its declared dimensions.", nameof(image));

    if (!traits.IsIndexed)
      return;

    if (image.PaletteCount > traits.MaximumPaletteEntries)
      throw new ArgumentException(
        $"The palette has {image.PaletteCount} entries but {traits.IndexBitDepth}-bit indices can address at most {traits.MaximumPaletteEntries}.",
        nameof(image));

    var storageTraits = Get(image.Format);
    if (storageTraits.IndexBitDepth == traits.IndexBitDepth)
      return;

    var pixelCount = checked(image.Width * image.Height);
    var maximumIndex = traits.MaximumPaletteEntries - 1;

    if (image.Format == PixelFormat.Indexed8) {
      for (var i = 0; i < pixelCount; ++i)
        if (image.PixelData[i] > maximumIndex)
          throw new ArgumentException(
            $"Pixel {i} uses palette index {image.PixelData[i]}, outside the 0..{maximumIndex} range of an Indexed{traits.IndexBitDepth} image.",
            nameof(image));
      return;
    }

    if (image.Format == PixelFormat.Indexed16) {
      for (var i = 0; i < pixelCount; ++i) {
        var offset = i * 2;
        var index = image.PixelData[offset] | image.PixelData[offset + 1] << 8;
        if (index > maximumIndex)
          throw new ArgumentException(
            $"Pixel {i} uses palette index {index}, outside the 0..{maximumIndex} range of an Indexed{traits.IndexBitDepth} image.",
            nameof(image));
      }
    }
  }

  private static RawPixelFormatTraits _Packed(
    PixelFormat format,
    int storageBitsPerPixel,
    int bytesPerPixel,
    int componentBitDepth,
    RawPixelAlphaKind alpha = RawPixelAlphaKind.None
  ) => new(format, RawPixelFormatKind.PackedInteger, storageBitsPerPixel, bytesPerPixel, componentBitDepth, alpha);

  private static RawPixelFormatTraits _Floating(
    PixelFormat format,
    int storageBitsPerPixel,
    int bytesPerPixel,
    int componentBitDepth,
    RawPixelAlphaKind alpha = RawPixelAlphaKind.None
  ) => new(format, RawPixelFormatKind.FloatingPoint, storageBitsPerPixel, bytesPerPixel, componentBitDepth, alpha);

  private static RawPixelFormatTraits _Indexed(int bitDepth, PixelFormat legacyFormat, int storageBitsPerPixel, int bytesPerPixel)
    => new(
      legacyFormat,
      RawPixelFormatKind.Indexed,
      storageBitsPerPixel,
      bytesPerPixel,
      0,
      RawPixelAlphaKind.Palette,
      IndexBitDepth: bitDepth
    );

  private static RawPixelFormatTraits _Yuv(
    PixelFormat format,
    int storageBitsPerPixel,
    int componentBitDepth,
    int chromaSubsampleX,
    int chromaSubsampleY
  ) => new(
    format,
    RawPixelFormatKind.PlanarYuv,
    storageBitsPerPixel,
    0,
    componentBitDepth,
    PlaneCount: 3,
    ChromaSubsampleX: chromaSubsampleX,
    ChromaSubsampleY: chromaSubsampleY
  );
}
