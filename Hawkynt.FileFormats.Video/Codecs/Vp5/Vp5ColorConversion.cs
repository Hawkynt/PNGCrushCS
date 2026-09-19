namespace FileFormat.Codecs.Vp5;

/// <summary>
/// Turns a decoded VP5 picture the right way up and converts it to packed RGB.
/// </summary>
/// <remarks>
/// Neither step is part of the codec. VP5 codes 4:2:0 samples bottom row first and says nothing about
/// which colour primaries they are for, so a reader that hands back RGB has had to choose. The choices
/// here are the ones the VP3, VP8 and MPEG-1 decoders in this package already make, for the same
/// reasons: luminance runs 16 to 235 and chrominance 16 to 240, so the conversion expands that range
/// rather than reading the samples as though they filled the byte, and the half-size chrominance
/// planes are interpolated rather than repeated.
/// <para/>
/// The interpolation is where this parts company with FFmpeg, whose <c>yuv420p</c> to <c>rgb24</c>
/// path repeats each chrominance sample across the two-by-two square of luminance samples it covers.
/// A hard colour edge therefore comes out of FFmpeg as a step and out of this as a ramp, and an RGB
/// comparison between the two has a floor that grows with how much saturated colour the picture holds.
/// That is why the oracle test for this codec compares the sample planes and not the RGB: the planes
/// are the decode and the RGB is a display convention.
/// </remarks>
internal static class Vp5ColorConversion {

  internal static byte[] ToRgb24(Vp5Frame frame, int width, int height) {
    // Turned the right way up before anything else, because the chrominance interpolation below is
    // direction-dependent: a luminance row is a quarter of a chrominance step from the row covering
    // it and three quarters from the next one along, and which row is "the next one" depends on which
    // way up the picture is.
    var luma = frame.TopDown(0);
    var cb = frame.TopDown(1);
    var cr = frame.TopDown(2);
    var lumaWidth = frame.LumaWidth;
    var chromaWidth = frame.ChromaWidth;
    var chromaHeight = frame.ChromaHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * lumaWidth;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var blueDifference = _Chroma(cb, chromaWidth, chromaHeight, x, y) - 128;
        var redDifference = _Chroma(cr, chromaWidth, chromaHeight, x, y) - 128;

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

  /// <summary>
  /// One chrominance sample at a luminance position, interpolated from the four around it.
  /// </summary>
  /// <remarks>
  /// A chrominance sample sits at the centre of the two-by-two square of luminance samples it covers,
  /// so a luminance sample lies a quarter of a chrominance step from the sample covering it and three
  /// quarters from the next along. Three parts of the near sample to one of the far one is that
  /// distance written as weights; 9:3:3:1 over the four is the separable form of it. Past the edge of
  /// the plane the near sample is used for both, which replicates the edge rather than wrapping.
  /// </remarks>
  private static int _Chroma(byte[] plane, int planeWidth, int planeHeight, int x, int y) {
    var nearX = x >> 1;
    var nearY = y >> 1;
    var farX = _Neighbour(nearX, x, planeWidth);
    var farY = _Neighbour(nearY, y, planeHeight);

    var nearNear = plane[nearY * planeWidth + nearX];
    var nearFar = plane[nearY * planeWidth + farX];
    var farNear = plane[farY * planeWidth + nearX];
    var farFar = plane[farY * planeWidth + farX];

    return (9 * nearNear + 3 * nearFar + 3 * farNear + farFar + 8) >> 4;
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
