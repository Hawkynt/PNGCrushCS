namespace FileFormat.Core;

/// <summary>Describes the pixel layout and bit depth of raw image data.</summary>
public enum PixelFormat {
  Bgra32,
  Rgba32,
  Argb32,
  Rgb24,
  Bgr24,
  Gray8,
  Gray16,
  GrayAlpha16,

  /// <summary>
  /// Grey and alpha at sixteen bits each, big-endian, four bytes a pixel.
  /// </summary>
  /// <remarks>
  /// PNG's fourth colour type at a depth of sixteen. Every other combination PNG allows had a format
  /// here and this one did not, so a sixteen-bit grey-with-alpha PNG could not be opened at all —
  /// not narrowed, refused. Byte order is the network order PNG stores, as with
  /// <see cref="Gray16"/> and <see cref="Rgba64"/>.
  /// </remarks>
  GrayAlpha32,

  Indexed8,
  Indexed4,
  Indexed1,
  /// <summary>Indexed image with 16-bit little-endian indices into a palette of up to 65 536 entries.
  /// Use this for formats whose native palette exceeds 256 colours (CGA Reenigne 1024-mode, 10-bit
  /// indexed scientific scans, anything wanting 9–16-bit indexed depth).</summary>
  Indexed16,
  Rgba64,
  Rgb48,
  Rgb565,
  /// <summary>10-bit grayscale stored right-justified in a 16-bit little-endian container — value
  /// range 0..1023, top 6 bits zero. Distinct from <see cref="Gray16"/> so consumers know the source
  /// precision and don't render 10-bit data as if it were 16-bit (which would look ~64× darker).</summary>
  Gray10,
  /// <summary>32-bit packed RGB with 10 bits per channel + 2-bit alpha (or unused).
  /// Layout matches <c>DXGI_FORMAT_R10G10B10A2_UNORM</c> and Vulkan <c>VK_FORMAT_A2B10G10R10_UNORM_PACK32</c>:
  /// little-endian uint32 with R in bits 0..9, G in 10..19, B in 20..29, A in 30..31. Default for
  /// HDR pipelines, HEIF 10-bit, AVIF main10, ProRes 10-bit RGB.</summary>
  Rgb30,
}
