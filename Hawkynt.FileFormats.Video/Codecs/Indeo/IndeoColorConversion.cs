using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Turns the three planes Indeo 2 and Indeo 3 both code — luminance at full size, two chrominance
/// planes at a quarter of it in each direction — into packed RGB for display.
/// </summary>
/// <remarks>
/// Neither codec says anything about which primaries its samples are meant for, so a reader that hands
/// back RGB has had to choose; the choice here is the one the rest of this package makes, ITU-R BT.601
/// with studio swing, so that luminance running 16 to 235 and chrominance 16 to 240 is expanded rather
/// than read as though it filled the byte.
/// <para/>
/// <b>A chrominance sample is repeated over the whole four-by-four square of luminance samples it
/// covers</b> rather than interpolated across it. At 4:1:0 a chrominance plane is a sixteenth of the
/// picture, and interpolating it would draw ramps sixteen samples wide that no encoder put there;
/// repeating keeps the colour where the codec placed it, and is also what ffmpeg's own conversion of
/// these two codecs does, so an RGB comparison against ffmpeg has no upsampling difference in it.
/// <para/>
/// This is display and not decode. What settles whether a bitstream was read correctly is the sample
/// planes, which is where both decoders are measured against ffmpeg.
/// </remarks>
internal static class IndeoColorConversion {

  /// <summary>Builds a packed RGB picture from one decoded frame's three planes.</summary>
  /// <param name="luma">The luminance plane, <paramref name="width"/> samples a row.</param>
  /// <param name="cb">The blue-difference plane, <paramref name="chromaWidth"/> samples a row.</param>
  /// <param name="cr">The red-difference plane, laid out like <paramref name="cb"/>.</param>
  /// <param name="width">The picture's width in samples.</param>
  /// <param name="height">The picture's height in samples.</param>
  /// <param name="chromaWidth">The chrominance planes' width, a quarter of the picture's rounded up.</param>
  /// <param name="chromaHeight">The chrominance planes' height.</param>
  internal static byte[] ToRgb24(
    ReadOnlySpan<byte> luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr,
    int width, int height, int chromaWidth, int chromaHeight) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * width;
      var chromaRow = Math.Min(y >> 2, chromaHeight - 1) * chromaWidth;
      var target = lumaRow * 3;

      for (var x = 0; x < width; ++x) {
        var chromaAt = chromaRow + Math.Min(x >> 2, chromaWidth - 1);
        var blueDifference = cb[chromaAt] - 128;
        var redDifference = cr[chromaAt] - 128;

        // ITU-R BT.601 with studio swing, in 8-bit fixed point:
        // 1.164 = 298/256, 1.596 = 409/256, 0.391 = 100/256, 0.813 = 208/256, 2.017 = 516/256.
        var scaledLuma = 298 * (luma[lumaRow + x] - 16);

        rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        target += 3;
      }
    }

    return rgb;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
  }
}
