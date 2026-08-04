using FileFormat.Core;

namespace FileFormat.Dds;

/// <summary>
/// Which byte of an uncompressed DDS pixel is which colour, worked out from the header's masks.
/// </summary>
/// <remarks>
/// An uncompressed surface does not have one fixed layout: the header states a mask per channel and
/// the bytes mean whatever those say. Nearly every file in the wild is A8R8G8B8 — masks of
/// 0x00FF0000, 0x0000FF00, 0x000000FF and 0xFF000000 — which puts blue in the lowest byte and so
/// lays the pixel out in memory as blue, green, red, alpha.
/// <para/>
/// This was read as red, green, blue, alpha regardless of the masks, which turns every red in a
/// texture blue and every blue red. Nothing caught it because the writer emitted the same masks and
/// the same wrong byte order, so the two halves of this project agreed with each other; ImageMagick
/// and XnView both disagree, and a file ImageMagick had written itself decoded here as its own
/// negative in the two channels.
/// </remarks>
public enum DdsChannelOrder {

  /// <summary>The masks were not one of the arrangements known here.</summary>
  Unknown = 0,

  /// <summary>Blue, green, red — the ordinary 24-bit DDS.</summary>
  Bgr,

  /// <summary>Red, green, blue.</summary>
  Rgb,

  /// <summary>Blue, green, red, alpha — A8R8G8B8, which is most of what exists.</summary>
  Bgra,

  /// <summary>Red, green, blue, alpha.</summary>
  Rgba,
}

/// <summary>Turns a header's channel masks into the order its bytes actually sit in.</summary>
internal static class DdsChannelOrderExtensions {

  /// <summary>
  /// The pixel format holding bytes in this order, falling back on the arrangement almost every file
  /// uses when the masks were not recognised.
  /// </summary>
  public static PixelFormat ToPixelFormat(this DdsChannelOrder order, int bytesPerPixel) => order switch {
    DdsChannelOrder.Bgr => PixelFormat.Bgr24,
    DdsChannelOrder.Rgb => PixelFormat.Rgb24,
    DdsChannelOrder.Bgra => PixelFormat.Bgra32,
    DdsChannelOrder.Rgba => PixelFormat.Rgba32,
    _ => bytesPerPixel == 3 ? PixelFormat.Bgr24 : PixelFormat.Bgra32,
  };

  /// <summary>
  /// Reads the masks as a byte order. A mask names the bits a channel occupies within a little-endian
  /// word, so the channel whose mask is lowest sits in the first byte.
  /// </summary>
  /// <remarks>
  /// Only the four arrangements that occur are recognised. A file masking its channels some other way
  /// — five bits of red in sixteen, say — is not one of these and is answered as unknown rather than
  /// guessed at, because a guess here is silently the wrong colours rather than a refusal.
  /// </remarks>
  public static DdsChannelOrder FromMasks(int red, int green, int blue, int alpha, int bitCount) {
    if (green != 0x0000FF00)
      return DdsChannelOrder.Unknown;

    return bitCount switch {
      24 when red == 0x00FF0000 && blue == 0x000000FF => DdsChannelOrder.Bgr,
      24 when red == 0x000000FF && blue == 0x00FF0000 => DdsChannelOrder.Rgb,
      32 when red == 0x00FF0000 && blue == 0x000000FF && alpha == unchecked((int)0xFF000000) => DdsChannelOrder.Bgra,
      32 when red == 0x000000FF && blue == 0x00FF0000 && alpha == unchecked((int)0xFF000000) => DdsChannelOrder.Rgba,
      _ => DdsChannelOrder.Unknown,
    };
  }
}
