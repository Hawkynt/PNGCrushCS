namespace FileFormat.Codecs.Vp9;

/// <summary>
/// Crops a decoded picture to the size its frame header stated and converts it to packed RGB.
/// </summary>
/// <remarks>
/// Neither step is part of the coding standard. VP9 codes 4:2:0 samples in whole superblocks and says
/// which colour primaries and which signal range they are meant for; what a display does with them is
/// the display's business, and a reader that hands back RGB has had to choose. The choices are the
/// same ones the VP8 and MPEG-1 decoders in this library make, for the same reasons — with the one
/// addition that VP9 states its signal range, so a full-range stream is converted as full range
/// rather than being stretched a second time.
/// <para/>
/// The half-size chrominance planes are interpolated rather than repeated, which is where this parts
/// company with ffmpeg: its <c>yuv420p</c> to <c>rgb24</c> path repeats each chrominance sample across
/// the two-by-two square of luminance samples it covers, so a hard colour edge comes out of ffmpeg as
/// a step and out of this as a ramp. That difference is in the display convention and not in the
/// decode: the sample planes this is handed match ffmpeg's exactly, which is the measurement that says
/// whether the bitstream was read correctly.
/// </remarks>
internal static class Vp9ColorConversion {

  internal static byte[] ToRgb24(Vp9Frame frame, bool fullRange) {
    var width = frame.Width;
    var height = frame.Height;
    var rgb = new byte[width * height * 3];

    // The edge the interpolation replicates is the picture's, not the buffer's. A VP9 frame buffer is
    // whole superblocks and so reaches well past the stated picture; the samples out there are real
    // coded samples that later frames predict from, but they are not part of this picture and
    // dragging them into its right-hand or bottom column would put colour on screen that the film
    // does not show.
    var chromaWidth = (width + 1) >> 1;
    var chromaHeight = (height + 1) >> 1;

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * frame.LumaWidth;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = frame.Luma[lumaRow + x];
        var blueDifference = _Chroma(frame.Cb, frame.ChromaWidth, chromaWidth, chromaHeight, x, y) - 128;
        var redDifference = _Chroma(frame.Cr, frame.ChromaWidth, chromaWidth, chromaHeight, x, y) - 128;

        // ITU-R BT.601, in 8-bit fixed point. Studio swing runs luminance 16 to 235 and expands it;
        // full swing uses the whole byte and only needs the colour difference terms.
        // 1.164 = 298/256, 1.596 = 409/256, 0.391 = 100/256, 0.813 = 208/256, 2.017 = 516/256, and
        // for full swing 1.402 = 359/256, 0.344 = 88/256, 0.714 = 183/256, 1.772 = 454/256.
        int red;
        int green;
        int blue;

        if (fullRange) {
          var scaled = luma << 8;
          red = scaled + 359 * redDifference;
          green = scaled - 88 * blueDifference - 183 * redDifference;
          blue = scaled + 454 * blueDifference;
        } else {
          var scaled = 298 * (luma - 16);
          red = scaled + 409 * redDifference;
          green = scaled - 100 * blueDifference - 208 * redDifference;
          blue = scaled + 516 * blueDifference;
        }

        rgb[target] = _Clamp(red + 128);
        rgb[target + 1] = _Clamp(green + 128);
        rgb[target + 2] = _Clamp(blue + 128);
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
  private static int _Chroma(ushort[] plane, int stride, int width, int height, int x, int y) {
    var nearX = x >> 1;
    var nearY = y >> 1;
    var farX = _Neighbour(nearX, x, width);
    var farY = _Neighbour(nearY, y, height);

    var topLeft = plane[nearY * stride + nearX];
    var topRight = plane[nearY * stride + farX];
    var bottomLeft = plane[farY * stride + nearX];
    var bottomRight = plane[farY * stride + farX];

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
