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

  // Floating-point formats. Samples are interleaved in channel order and stored little-endian.
  // Unlike the integer formats above, values are not implicitly clamped to 0..1: negative values,
  // values above one, infinities and NaNs are representable because HDR/scientific formats use them.
  /// <summary>One IEEE 754 binary16 grey sample per pixel, little-endian.</summary>
  GrayF16,
  /// <summary>IEEE 754 binary16 grey + alpha, interleaved GA, little-endian.</summary>
  GrayAlphaF16,
  /// <summary>IEEE 754 binary16 RGB, interleaved RGB, little-endian.</summary>
  RgbF16,
  /// <summary>IEEE 754 binary16 RGBA, interleaved RGBA, little-endian.</summary>
  RgbaF16,
  /// <summary>One IEEE 754 binary32 grey sample per pixel, little-endian.</summary>
  GrayF32,
  /// <summary>IEEE 754 binary32 grey + alpha, interleaved GA, little-endian.</summary>
  GrayAlphaF32,
  /// <summary>IEEE 754 binary32 RGB, interleaved RGB, little-endian.</summary>
  RgbF32,
  /// <summary>IEEE 754 binary32 RGBA, interleaved RGBA, little-endian.</summary>
  RgbaF32,

  // Canonical planar YUV. PixelData is tightly packed Y, then U/Cb, then V/Cr, with no row padding.
  // The P10/P12/P16 variants store every sample in a little-endian ushort container; P10/P12 values
  // are right-justified so their numeric value is the coded sample value rather than a transport
  // convention such as P010's left alignment. Chroma plane dimensions round up for odd sizes.
  /// <summary>Planar 8-bit YUV 4:2:0: Y, U, V.</summary>
  Yuv420P8,
  /// <summary>Planar 8-bit YUV 4:2:2: Y, U, V.</summary>
  Yuv422P8,
  /// <summary>Planar 8-bit YUV 4:4:0: Y, U, V.</summary>
  Yuv440P8,
  /// <summary>Planar 8-bit YUV 4:4:4: Y, U, V.</summary>
  Yuv444P8,
  /// <summary>Planar 10-bit YUV 4:2:0 in right-justified little-endian ushort samples.</summary>
  Yuv420P10,
  /// <summary>Planar 10-bit YUV 4:2:2 in right-justified little-endian ushort samples.</summary>
  Yuv422P10,
  /// <summary>Planar 10-bit YUV 4:4:0 in right-justified little-endian ushort samples.</summary>
  Yuv440P10,
  /// <summary>Planar 10-bit YUV 4:4:4 in right-justified little-endian ushort samples.</summary>
  Yuv444P10,
  /// <summary>Planar 12-bit YUV 4:2:0 in right-justified little-endian ushort samples.</summary>
  Yuv420P12,
  /// <summary>Planar 12-bit YUV 4:2:2 in right-justified little-endian ushort samples.</summary>
  Yuv422P12,
  /// <summary>Planar 12-bit YUV 4:4:0 in right-justified little-endian ushort samples.</summary>
  Yuv440P12,
  /// <summary>Planar 12-bit YUV 4:4:4 in right-justified little-endian ushort samples.</summary>
  Yuv444P12,
  /// <summary>Planar 16-bit YUV 4:2:0 in little-endian ushort samples.</summary>
  Yuv420P16,
  /// <summary>Planar 16-bit YUV 4:2:2 in little-endian ushort samples.</summary>
  Yuv422P16,
  /// <summary>Planar 16-bit YUV 4:4:0 in little-endian ushort samples.</summary>
  Yuv440P16,
  /// <summary>Planar 16-bit YUV 4:4:4 in little-endian ushort samples.</summary>
  Yuv444P16,
}
