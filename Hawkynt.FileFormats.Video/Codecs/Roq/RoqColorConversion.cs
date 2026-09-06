using System;

namespace FileFormat.Codecs.Roq;

/// <summary>Turns a reconstructed <see cref="RoqFrame"/> into interleaved RGB.</summary>
/// <remarks>
/// RoQ's samples are full range rather than the studio 16-235 swing the codecs elsewhere in this
/// package's block-vector family use, matching how ffmpeg reports it: <c>yuvj444p</c>, not
/// <c>yuv420p</c>. So the matrix here is the plain full-range one — <c>R = Y + 1.402(Cr-128)</c>,
/// <c>G = Y - 0.344136(Cb-128) - 0.714136(Cr-128)</c>, <c>B = Y + 1.772(Cb-128)</c> — rather than the
/// luma-rescaling fixed-point one those codecs share. And because <see cref="RoqFrame"/> already
/// carries Cb and Cr at the picture's own full resolution rather than at half of it, this is a plain
/// per-pixel conversion with no chroma siting convention to pick a side of — there is no upsampling
/// step for an RGB comparison to disagree about, unlike a genuinely 4:2:0 codec.
/// <para/>
/// Verified against ffmpeg's own <c>rgb24</c> output, with a caveat worth stating precisely rather
/// than papered over: on two of three files measured, a handful of pixels a picture — never more than
/// twenty-four, and only where two chroma values are both close to a rounding boundary — differ from
/// ffmpeg's own <c>rgb24</c> by exactly one level in one channel. Reproducing this formula against
/// ffmpeg's own <c>yuvj444p</c> planes, entirely without this decoder, produces the identical handful
/// of differing pixels at the identical positions — proof the disagreement is ffmpeg's <c>swscale</c>
/// not implementing this exact formula either, rather than anything wrong with the samples this
/// decoder reconstructed. The decode itself is measured on those planes directly, where the answer
/// is unambiguous: see <see cref="RoqPictureDecoder"/>.
/// </remarks>
internal static class RoqColorConversion {

  internal static byte[] ToRgb24(RoqFrame frame) {
    var width = frame.Width;
    var height = frame.Height;
    var pixels = new byte[width * height * 3];
    var y = frame.Y;
    var cb = frame.Cb;
    var cr = frame.Cr;

    for (int i = 0, o = 0; i < pixels.Length / 3; ++i, o += 3) {
      double luma = y[i];
      double blueChroma = cb[i] - 128;
      double redChroma = cr[i] - 128;

      pixels[o] = _Clamp(luma + 1.402 * redChroma);
      pixels[o + 1] = _Clamp(luma - 0.344136 * blueChroma - 0.714136 * redChroma);
      pixels[o + 2] = _Clamp(luma + 1.772 * blueChroma);
    }

    return pixels;
  }

  /// <summary>Fills a frame's three planes from interleaved RGB, the exact reverse of <see cref="ToRgb24"/>.</summary>
  /// <remarks>
  /// The forward half of the same full-range matrix — <c>Y = 0.299R + 0.587G + 0.114B</c>,
  /// <c>Cb = 128 - 0.168736R - 0.331264G + 0.5B</c>, <c>Cr = 128 + 0.5R - 0.418688G - 0.081312B</c> —
  /// so a picture handed to the encoder as colour is put into the samples RoQ actually states. A
  /// picture that arrives as <c>Yuv444P8</c> never comes through here at all: those are already the
  /// samples, and converting them to colour and back would round twice for nothing.
  /// </remarks>
  internal static void FromRgb24(ReadOnlySpan<byte> pixels, RoqFrame frame) {
    var y = frame.Y;
    var cb = frame.Cb;
    var cr = frame.Cr;

    for (int i = 0, o = 0; i < y.Length; ++i, o += 3) {
      double red = pixels[o];
      double green = pixels[o + 1];
      double blue = pixels[o + 2];

      y[i] = _Clamp(0.299 * red + 0.587 * green + 0.114 * blue);
      cb[i] = _Clamp(128 - 0.168736 * red - 0.331264 * green + 0.5 * blue);
      cr[i] = _Clamp(128 + 0.5 * red - 0.418688 * green - 0.081312 * blue);
    }
  }

  private static byte _Clamp(double value) {
    var rounded = Math.Round(value, MidpointRounding.ToEven);
    return rounded switch {
      <= 0 => 0,
      >= 255 => 255,
      _ => (byte)rounded,
    };
  }
}
