using FileFormat.Core;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// Turns the reconstructed channels into the packed 8-bit colour every reader here hands back.
/// </summary>
/// <remarks>
/// A display step and not part of decoding, exactly as in <c>ProResColorConversion</c>: the channels
/// this reads are already the coded picture, at ten or twelve bits, and everything here — narrowing to
/// eight bits, choosing BT.601 or BT.709, resampling a horizontally-subsampled chroma channel up to
/// every luma column — is a convention two correct decoders are free to disagree about, which is why a
/// comparison against another decoder belongs on the channels themselves (see
/// <see cref="CineFormVideoDecoder.DecodeChannels"/>) and never on this method's output.
/// </remarks>
internal static class CineFormColorConversion {

  /// <summary>Packs a 4:2:2 YUV frame (channel order Y, V, U — see
  /// <see cref="CineFormChannelDecoder"/>) into 8-bit RGB.</summary>
  internal static byte[] YuvToRgb24(CineFormPictureDecoder.Result frame) {
    var width = frame.ImageWidth;
    var height = frame.ImageHeight;
    var luma = frame.Channels[0];
    var v = frame.Channels[1];
    var u = frame.Channels[2];
    const int bitDepth = 10;
    const int extra = bitDepth - 8;
    const int black = 16 << extra;
    const int centre = 128 << extra;
    const int shift = 8 + extra;
    const int half = 1 << (shift - 1);
    const int redFromCr = 459, greenFromCb = 55, greenFromCr = 136, blueFromCb = 541; // BT.709, ProResColorConversion's own table

    var rgb = new byte[width * height * 3];
    var subsampled = luma.Width != u.Width;
    var lastChromaColumn = u.Width - 1;

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * luma.Width;
      var chromaRow = y * u.Width;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var scaledLuma = 298 * (luma.Samples[lumaRow + x] - black);
        var blueDifference = _Chroma(u.Samples, chromaRow, x, subsampled, lastChromaColumn) - centre;
        var redDifference = _Chroma(v.Samples, chromaRow, x, subsampled, lastChromaColumn) - centre;

        rgb[target] = _Clamp(scaledLuma + redFromCr * redDifference + half, shift);
        rgb[target + 1] = _Clamp(scaledLuma - greenFromCb * blueDifference - greenFromCr * redDifference + half, shift);
        rgb[target + 2] = _Clamp(scaledLuma + blueFromCb * blueDifference + half, shift);
        target += 3;
      }
    }

    return rgb;
  }

  /// <summary>Packs an RGB frame (channel order G, R, B — see <see cref="CineFormChannelDecoder"/>) at
  /// twelve bits into 8-bit RGB.</summary>
  /// <remarks>
  /// <see cref="ChannelScaling.Reduce16"/> narrows a value that fills its declared sixteen-bit range,
  /// which is only true here because <see cref="CineFormPictureDecoder.Decode"/> has already clamped
  /// every channel sample to the twelve bits it is coded at — see that method's remarks for why a
  /// wavelet reconstruction needs that clamp at all. A sample handed to this method unclamped and
  /// negative, or above 4095, would shift out of range and have its reduction's own out-of-range
  /// result silently wrapped by the <c>byte</c> cast, which is a fault this method relies on not
  /// happening rather than one it guards against itself.
  /// </remarks>
  internal static byte[] RgbToRgb24(CineFormPictureDecoder.Result frame) {
    var width = frame.ImageWidth;
    var height = frame.ImageHeight;
    var g = frame.Channels[0];
    var r = frame.Channels[1];
    var b = frame.Channels[2];

    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y) {
      var row = y * g.Width;
      var target = y * width * 3;
      for (var x = 0; x < width; ++x) {
        rgb[target] = ChannelScaling.Reduce16(r.Samples[row + x] << 4);
        rgb[target + 1] = ChannelScaling.Reduce16(g.Samples[row + x] << 4);
        rgb[target + 2] = ChannelScaling.Reduce16(b.Samples[row + x] << 4);
        target += 3;
      }
    }

    return rgb;
  }

  private static int _Chroma(int[] plane, int row, int x, bool subsampled, int lastColumn) {
    if (!subsampled)
      return plane[row + x];

    var near = x >> 1;
    if ((x & 1) == 0)
      return plane[row + near];

    var far = near + 1 <= lastColumn ? near + 1 : lastColumn;
    return (plane[row + near] + plane[row + far] + 1) >> 1;
  }

  private static byte _Clamp(int value, int shift) {
    var v = value >> shift;
    return (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
  }
}
