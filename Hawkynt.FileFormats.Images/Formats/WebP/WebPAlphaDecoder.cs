using System;
using System.IO;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP;

/// <summary>Turns an ALPH chunk into a flat alpha plane, one byte per pixel, top-left origin.</summary>
/// <remarks>
/// VP8 lossy carries no alpha of its own, so a lossy picture that has any keeps it in a separate
/// ALPH chunk beside the VP8 one. The chunk is one flag byte and then the plane, and the flag byte
/// decides how the plane is stored:
/// <list type="bullet">
///   <item>compression 0 — the bytes are the plane, in order;</item>
///   <item>compression 1 — the plane is a VP8L image stream whose green channel holds the alpha.</item>
/// </list>
/// Compression 1 is what <c>cwebp</c> writes by default: a plain <c>cwebp -q 80</c> of anything with
/// transparency produces flag byte 0x01. Treating it as undecodable and calling the picture opaque —
/// which is what happened here before — is the worst available answer, because an opaque version of
/// a transparent picture looks entirely reasonable until something is composited against it.
/// </remarks>
internal static class WebPAlphaDecoder {

  private const int _COMPRESSION_NONE = 0;
  private const int _COMPRESSION_LOSSLESS = 1;

  private const int _PREPROCESSING_LEVEL_REDUCTION = 1;

  /// <summary>Decodes an ALPH chunk. Throws when the chunk states something this cannot reproduce
  /// exactly, rather than answering with a plane that is nearly right.</summary>
  public static byte[] Decode(byte[] chunk, int width, int height) {
    ArgumentNullException.ThrowIfNull(chunk);
    if (chunk.Length < 1)
      throw new InvalidDataException("ALPH chunk is empty.");

    var flags = chunk[0];
    var compression = flags & 0x03;
    var filter = (flags >> 2) & 0x03;
    var preProcessing = (flags >> 4) & 0x03;

    var pixelCount = width * height;
    byte[] plane;

    switch (compression) {
      case _COMPRESSION_NONE:
        if (chunk.Length < 1 + pixelCount)
          throw new InvalidDataException(
            $"ALPH chunk holds {chunk.Length - 1} uncompressed alpha bytes for a {width}x{height} picture needing {pixelCount}.");
        plane = new byte[pixelCount];
        Buffer.BlockCopy(chunk, 1, plane, 0, pixelCount);
        break;

      case _COMPRESSION_LOSSLESS: {
        // The stream has no VP8L preamble — its size is the picture's, and the alpha values ride in
        // the green channel, which is where the encoder put them so that the subtract-green and
        // predictor transforms would have something worth working on.
        var argb = Vp8LDecoder.DecodeArgbStream(chunk, 1, width, height);
        plane = new byte[pixelCount];
        for (var i = 0; i < pixelCount; ++i)
          plane[i] = (byte)((argb[i] >> 8) & 0xFF);
        break;
      }

      default:
        throw new NotSupportedException($"ALPH compression method {compression} is not defined by the WebP container specification.");
    }

    _Unfilter(plane, width, height, filter);

    // Level reduction is a lossy pre-pass the encoder applies when asked for a cheaper alpha plane;
    // undoing it needs libwebp's DequantizeLevels smoothing, which is not implemented here. Reporting
    // the un-smoothed plane would be a picture that is close and wrong, so this refuses instead.
    if (preProcessing == _PREPROCESSING_LEVEL_REDUCTION)
      throw new NotSupportedException("ALPH chunk uses level-reduction pre-processing, which this decoder cannot undo.");

    return plane;
  }

  /// <summary>Reverses the row filter the ALPH flag byte names.</summary>
  /// <remarks>
  /// These are libwebp's <c>HorizontalUnfilter_C</c>, <c>VerticalUnfilter_C</c> and
  /// <c>GradientUnfilter_C</c> from <c>src/dsp/filters.c</c>. All three fall back to the horizontal
  /// one on the first row, where there is no row above to predict from.
  /// </remarks>
  private static void _Unfilter(byte[] plane, int width, int height, int filter) {
    if (filter == 0 || width <= 0 || height <= 0)
      return;

    for (var y = 0; y < height; ++y) {
      var row = y * width;
      var above = row - width;
      var effective = y == 0 ? 1 : filter; // no row above: every filter degrades to horizontal

      switch (effective) {
        case 1:
          plane[row] += y == 0 ? (byte)0 : plane[above];
          for (var x = 1; x < width; ++x)
            plane[row + x] += plane[row + x - 1];
          break;

        case 2:
          for (var x = 0; x < width; ++x)
            plane[row + x] += plane[above + x];
          break;

        case 3: {
          var left = plane[above];
          var topLeft = left;
          for (var x = 0; x < width; ++x) {
            var top = plane[above + x];
            left = (byte)(plane[row + x] + _GradientPredictor(left, top, topLeft));
            topLeft = top;
            plane[row + x] = left;
          }
          break;
        }
      }
    }
  }

  private static int _GradientPredictor(byte left, byte top, byte topLeft) {
    var g = left + top - topLeft;
    return (g & ~0xFF) == 0 ? g : g < 0 ? 0 : 255;
  }
}
