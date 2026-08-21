namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Crops a decoded picture to the size its key frame stated and converts it to packed RGB.
/// </summary>
/// <remarks>
/// Neither step is part of the coding standard. VP8 codes 4:2:0 samples in whole macroblocks and
/// says which colour primaries they are meant for; what a display does with them is the display's
/// business, and a reader that hands back RGB has had to choose. The choices are the same ones the
/// MPEG-1 decoder in this library makes, for the same reasons: luminance runs 16 to 235 and
/// chrominance 16 to 240, so the conversion expands that range rather than reading the samples as
/// though they filled the byte, and the half-size chrominance planes are interpolated rather than
/// repeated.
/// <para/>
/// The interpolation is where this parts company with ffmpeg, whose <c>yuv420p</c> to <c>rgb24</c>
/// path repeats each chrominance sample across the two-by-two square of luminance samples it covers.
/// A hard colour edge therefore comes out of ffmpeg as a step and out of this as a ramp. That
/// difference is in the display convention and not in the decode: the sample planes this is handed
/// match ffmpeg's exactly, which is the measurement that says whether the bitstream was read
/// correctly.
/// </remarks>
internal static class Vp8ColorConversion {

  internal static byte[] ToRgb24(Vp8Frame frame, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * frame.LumaWidth;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = frame.Luma[lumaRow + x];
        var blueDifference = _Chroma(frame.Cb, frame.ChromaWidth, frame.ChromaHeight, x, y) - 128;
        var redDifference = _Chroma(frame.Cr, frame.ChromaWidth, frame.ChromaHeight, x, y) - 128;

        // ITU-R BT.601 with studio swing, in 8-bit fixed point:
        // 1.164 = 298/256, 1.596 = 409/256, 0.391 = 100/256, 0.813 = 208/256, 2.017 = 516/256.
        var scaledLuma = 298 * (luma - 16);

        rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        target += 3;
      }
    }

    return rgb;
  }

  /// <summary>
  /// One chrominance sample at a luminance position, interpolated from the four around it.
  /// </summary>
  /// <remarks>
  /// A chrominance sample sits at the centre of the two-by-two square of luminance samples it covers,
  /// so a luminance sample lies a quarter of a chrominance step from the sample covering it and three
  /// quarters from the next along. Three parts of the near sample to one of the far one is that
  /// distance written as weights. Past the edge of the plane the near sample is used for both, which
  /// replicates the edge rather than wrapping.
  /// </remarks>
  private static int _Chroma(byte[] plane, int planeWidth, int planeHeight, int x, int y) {
    var nearX = x >> 1;
    var nearY = y >> 1;
    var farX = _Neighbour(nearX, x, planeWidth);
    var farY = _Neighbour(nearY, y, planeHeight);

    var topLeft = plane[nearY * planeWidth + nearX];
    var topRight = plane[nearY * planeWidth + farX];
    var bottomLeft = plane[farY * planeWidth + nearX];
    var bottomRight = plane[farY * planeWidth + farX];

    // 9:3:3:1 over the four, which is the separable form of three-to-one in each direction.
    return (9 * topLeft + 3 * topRight + 3 * bottomLeft + bottomRight + 8) >> 4;
  }

  private static int _Neighbour(int near, int luminance, int limit) {
    var far = (luminance & 1) == 0 ? near - 1 : near + 1;
    return far < 0 ? 0 : far >= limit ? limit - 1 : far;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
