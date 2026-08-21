namespace FileFormat.Codecs.Theora;

/// <summary>
/// Crops a decoded frame to its picture region, turns it the right way up, and converts it to packed
/// RGB.
/// </summary>
/// <remarks>
/// None of the three is part of the coding standard. Theora codes whole macro blocks in a
/// right-handed coordinate system and says which colour primaries the samples are meant for; what a
/// display does with them is the display's business, and a reader that hands back RGB has had to
/// choose.
/// <para/>
/// The crop is the one step the specification does insist on: the coded frame is a whole number of
/// macro blocks and the picture inside it may be any size at any offset, and the samples outside the
/// picture are real coded samples that later frames predict from — so they are decoded like any
/// other and dropped only here. The flip is Theora's alone: its origin is the lower-left corner
/// where a bitmap's is the upper-left, so row zero of the frame is the last row of the picture.
/// <para/>
/// The choices in the conversion itself are the same ones the VP8 and MPEG-1 decoders in this
/// library make, for the same reasons: luminance runs 16 to 235 and chrominance 16 to 240, so the
/// conversion expands that range rather than reading the samples as though they filled the byte, and
/// subsampled chrominance planes are interpolated rather than repeated.
/// <para/>
/// The interpolation is where this parts company with ffmpeg, whose <c>yuv420p</c> to <c>rgb24</c>
/// path repeats each chrominance sample across the samples it covers. A hard colour edge therefore
/// comes out of ffmpeg as a step and out of this as a ramp. That difference is in the display
/// convention and not in the decode: the sample planes this is handed match ffmpeg's exactly, which
/// is the measurement that says whether the bitstream was read correctly.
/// </remarks>
internal static class TheoraColorConversion {

  internal static byte[] ToRgb24(TheoraFrame frame, TheoraIdentificationHeader header) {
    var width = header.PictureWidth;
    var height = header.PictureHeight;
    var rgb = new byte[width * height * 3];

    var luma = frame.Planes[0];
    var lumaWidth = frame.Widths[0];
    var chromaWidth = frame.Widths[1];
    var chromaHeight = frame.Heights[1];

    // How many luma samples one chroma sample spans, which is one or two along each axis.
    var horizontalRatio = header.PixelFormat == TheoraPixelFormat.Yuv444 ? 1 : 2;
    var verticalRatio = header.PixelFormat == TheoraPixelFormat.Yuv420 ? 2 : 1;

    for (var row = 0; row < height; ++row) {
      // Theora's row zero is the bottom one, and a bitmap's is the top one.
      var frameRow = header.PictureY + height - 1 - row;
      var lumaAt = frameRow * lumaWidth + header.PictureX;
      var target = row * width * 3;

      for (var column = 0; column < width; ++column) {
        var frameColumn = header.PictureX + column;
        var blueDifference = _Chroma(frame.Planes[1], chromaWidth, chromaHeight, frameColumn, frameRow, horizontalRatio, verticalRatio) - 128;
        var redDifference = _Chroma(frame.Planes[2], chromaWidth, chromaHeight, frameColumn, frameRow, horizontalRatio, verticalRatio) - 128;

        // ITU-R BT.601 with studio swing, in 8-bit fixed point:
        // 1.164 = 298/256, 1.596 = 409/256, 0.391 = 100/256, 0.813 = 208/256, 2.017 = 516/256.
        var scaledLuma = 298 * (luma[lumaAt + column] - 16);

        rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        target += 3;
      }
    }

    return rgb;
  }

  /// <summary>
  /// One chrominance sample at a luminance position, interpolated from the samples around it.
  /// </summary>
  /// <remarks>
  /// A subsampled chrominance sample sits at the centre of the luminance samples it covers, so a
  /// luminance sample lies a quarter of a chrominance step from the sample covering it and three
  /// quarters from the next along — three parts of the near sample to one of the far one. Along an
  /// axis that is not subsampled the samples are co-sited and the near one is the whole answer. Past
  /// the edge of the plane the near sample is used for both, which replicates the edge rather than
  /// wrapping.
  /// </remarks>
  private static int _Chroma(
    byte[] plane, int planeWidth, int planeHeight, int x, int y, int horizontalRatio, int verticalRatio) {
    var (nearX, farX, nearWeightX) = _Neighbour(x, planeWidth, horizontalRatio);
    var (nearY, farY, nearWeightY) = _Neighbour(y, planeHeight, verticalRatio);

    var lowerLeft = plane[nearY * planeWidth + nearX];
    var lowerRight = plane[nearY * planeWidth + farX];
    var upperLeft = plane[farY * planeWidth + nearX];
    var upperRight = plane[farY * planeWidth + farX];

    var lower = nearWeightX * lowerLeft + (4 - nearWeightX) * lowerRight;
    var upper = nearWeightX * upperLeft + (4 - nearWeightX) * upperRight;
    return (nearWeightY * lower + (4 - nearWeightY) * upper + 8) >> 4;
  }

  /// <summary>The two chroma positions a luma position lies between, and the weight of the nearer.</summary>
  private static (int Near, int Far, int NearWeight) _Neighbour(int luma, int limit, int ratio) {
    if (ratio == 1)
      return (luma, luma, 4);

    var near = luma >> 1;
    var far = (luma & 1) == 0 ? near - 1 : near + 1;
    if (far < 0)
      far = 0;
    else if (far >= limit)
      far = limit - 1;

    return (near, far, 3);
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
