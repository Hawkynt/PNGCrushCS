namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// Turns a decoded picture into the packed RGB every reader in this library hands back.
/// </summary>
/// <remarks>
/// Two steps, and both of them are conventions rather than parts of the coding standard. The
/// standards code samples and say what colour primaries they are meant for (ITU-R BT.601), and it is
/// the display that decides what to do with them; a decoder that hands back RGB has had to choose.
/// <para/>
/// The choices here are the ones every player makes. Luminance runs 16 to 235 and chrominance 16 to
/// 240 rather than filling the byte, so the conversion expands that range — reading the samples as
/// though they filled it leaves every picture washed out by about seven per cent of its contrast,
/// which looks like a decode that worked. And the chrominance planes are smaller than the luminance
/// one, so bringing them back up is an interpolation between neighbours and not a repetition of each
/// sample across a block: repeating leaves a visible step at every second column, worst exactly
/// where colour changes fastest.
/// <para/>
/// Where the interpolation gets its weights from is the one part of this that is not a convention.
/// MPEG-1 sites a 4:2:0 chrominance sample at the centre of the two-by-two luminance square it
/// covers, so a luminance sample sits a quarter of a chrominance step away in each direction and the
/// weights are three to one both ways. MPEG-2 moved it: a 4:2:0 chrominance sample sits on the
/// even luminance column and half a row down (13818-2, Figure 6-4), so horizontally the even columns
/// take their chrominance sample unchanged and the odd ones sit exactly between two, while
/// vertically the three-to-one of MPEG-1 still applies. 4:2:2 is co-sited horizontally and has a
/// chrominance sample on every row, so only the odd columns are interpolated at all. Using MPEG-1's
/// weights on an MPEG-2 picture shifts every colour half a luminance sample to the left, which is
/// not visible on anything but a hard colour edge and is wrong on all of them.
/// <para/>
/// The matrix below was checked against ffmpeg's over a sweep of the whole luminance and chrominance
/// range at 4:4:4, where the two agree to the level everywhere but the very top of the range. The
/// interpolation is where they part: ffmpeg's <c>yuv420p</c> to <c>rgb24</c> path repeats each
/// chrominance sample across its two-by-two square, so a hard colour edge comes out of it as a step
/// and out of this as a ramp. That difference is in the display convention and not in the decode —
/// the sample planes this is handed match ffmpeg's exactly.
/// </remarks>
internal static class MpegColorConversion {

  /// <summary>
  /// Crops a decoded picture to its displayed size and converts it to packed 8-bit RGB.
  /// </summary>
  /// <param name="frame">The reconstructed planes, which are a whole number of macroblocks across.</param>
  /// <param name="width">The displayed width from the sequence header.</param>
  /// <param name="height">The displayed height.</param>
  /// <param name="isMpeg2">Which standard's chrominance siting to interpolate for.</param>
  internal static byte[] ToRgb24(MpegFrame frame, int width, int height, bool isMpeg2) {
    var rgb = new byte[width * height * 3];

    // 4:2:2 has a chrominance sample on every line, so there is nothing to interpolate vertically;
    // and MPEG-2 puts its chrominance on the even luminance column whichever format it is in.
    var fullHeight = frame.ChromaFormat != MpegChromaFormat.Yuv420;
    var coSitedHorizontally = isMpeg2;

    // How far the interpolation may reach, which is the displayed picture and not the coded one. The
    // planes run on past the crop — a 48-line interlaced sequence is coded as sixty-four — and those
    // rows hold real coded samples that later pictures may predict from, so they cannot simply not
    // be reconstructed. They are still not part of this picture: a sample at the bottom edge is
    // interpolated against the edge repeated, exactly as it would be by a display handed the cropped
    // planes, and not against whatever the encoder chose to pad the frame out with.
    var chromaWidth = frame.ChromaFormat == MpegChromaFormat.Yuv444 ? width : (width + 1) / 2;
    var chromaHeight = fullHeight ? height : (height + 1) / 2;

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * frame.LumaWidth;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = frame.Luma[lumaRow + x];
        var cb = _Chroma(
          frame.Cb, frame.ChromaWidth, chromaWidth, chromaHeight, x, y, coSitedHorizontally, fullHeight);
        var cr = _Chroma(
          frame.Cr, frame.ChromaWidth, chromaWidth, chromaHeight, x, y, coSitedHorizontally, fullHeight);

        // ITU-R BT.601 with studio swing, in 8-bit fixed point:
        // 1.164 = 298/256, 1.596 = 409/256, 0.391 = 100/256, 0.813 = 208/256, 2.017 = 516/256.
        var scaledLuma = 298 * (luma - 16);
        var blueDifference = cb - 128;
        var redDifference = cr - 128;

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
  /// All four cases are one weighted sum over sixteen rather than a chain of special cases, because
  /// the weights are the only thing that differs between them and writing four loops would be four
  /// places for the rounding to come out differently. Past the edge the near sample is used for both,
  /// which is a replication of the edge rather than a wrap.
  /// </remarks>
  private static int _Chroma(
    byte[] plane, int stride, int chromaWidth, int chromaHeight, int x, int y, bool coSitedHorizontally,
    bool fullHeight) {
    var nearX = x >> 1;
    var farX = _Neighbour(nearX, x, chromaWidth);

    // Three parts of the near sample to one of the far one is a quarter-step offset written as
    // weights; four to nothing is a sample that sits exactly on the luminance one; two to two is a
    // sample that sits exactly between two of them.
    var (nearWeightX, farWeightX) = coSitedHorizontally
      ? (x & 1) == 0 ? (4, 0) : (2, 2)
      : (3, 1);

    var nearY = fullHeight ? y : y >> 1;
    var farY = fullHeight ? nearY : _Neighbour(nearY, y, chromaHeight);
    var (nearWeightY, farWeightY) = fullHeight ? (4, 0) : (3, 1);

    var topLeft = plane[nearY * stride + nearX];
    var topRight = plane[nearY * stride + farX];
    var bottomLeft = plane[farY * stride + nearX];
    var bottomRight = plane[farY * stride + farX];

    var sum = nearWeightY * (nearWeightX * topLeft + farWeightX * topRight)
              + farWeightY * (nearWeightX * bottomLeft + farWeightX * bottomRight);

    return (sum + 8) >> 4;
  }

  /// <summary>The chrominance sample on the side the luminance sample leans towards, clamped to the plane.</summary>
  private static int _Neighbour(int near, int luminance, int limit) {
    var far = (luminance & 1) == 0 ? near - 1 : near + 1;
    return far < 0 ? 0 : far >= limit ? limit - 1 : far;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
