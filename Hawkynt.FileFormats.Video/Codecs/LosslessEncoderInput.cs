using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// The one question every lossless RGB encoder in this package asks of a picture before coding it:
/// can this be turned into eight-bit colour without losing anything, and if not, which format was
/// it that could not be.
/// </summary>
/// <remarks>
/// A lossless codec that quietly quantised a sixteen-bit or floating-point picture, or ran a YUV
/// picture through a colour matrix, would decode to something other than what it was given and
/// call the result exact. So the routes accepted here are exactly the ones that lose no sample
/// value: eight-bit RGB in any byte order, grey, palette lookups, and 5-6-5 widened to eight bits.
/// An alpha channel is dropped where the codec has no place for one, since alpha is not colour and
/// every encoder here that discards it says so in its own remarks.
/// </remarks>
internal static class LosslessEncoderInput {

  /// <summary>Checks the picture's geometry and payload, then converts it to <paramref name="target"/>
  /// where that conversion loses nothing, refusing by name where it would.</summary>
  public static RawImage Prepare(RawImage frame, PixelFormat target, int width, int height, string codecName) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != width || frame.Height != height)
      throw new InvalidDataException(
        $"{codecName} geometry is fixed at {width}x{height} for the life of the stream; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    if (frame.Format == target)
      return frame;

    if (!_ConvertsLosslesslyToEightBitColour(frame.Format))
      throw new NotSupportedException(
        $"{codecName} is lossless and codes eight-bit colour; a {frame.Format} picture cannot be converted to it without "
        + "changing sample values, so it is refused rather than quantised.");

    return FastRawImageConverter.Convert(frame, target);
  }

  private static bool _ConvertsLosslesslyToEightBitColour(PixelFormat format) => format is
    PixelFormat.Bgr24 or PixelFormat.Rgb24
    or PixelFormat.Bgra32 or PixelFormat.Rgba32 or PixelFormat.Argb32
    or PixelFormat.Gray8 or PixelFormat.GrayAlpha16
    or PixelFormat.Indexed8 or PixelFormat.Indexed4 or PixelFormat.Indexed1 or PixelFormat.Indexed16
    or PixelFormat.Rgb565;
}
