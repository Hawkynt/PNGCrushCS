namespace FileFormat.Codecs.H263;

/// <summary>
/// Turns a decoded 4:2:0 picture into the packed RGB every reader in this library hands back.
/// </summary>
/// <remarks>
/// Two steps, and both of them are conventions rather than parts of the coding standard. ITU-T H.263
/// codes samples and says what they are meant for (ITU-R BT.601 primaries, clause 4.1), and it is the
/// display that decides what to do with them; a decoder that hands back RGB has had to choose.
/// <para/>
/// The choices here are the ones every player makes, and they are the same two the MPEG-1 decoder in
/// this library makes, because the two formats state the same thing about their samples. Luminance
/// runs 16 to 235 and chrominance 16 to 240 rather than filling the byte, so the conversion expands
/// that range — reading the samples as though they filled it leaves every picture washed out by about
/// seven per cent of its contrast, which looks like a decode that worked. And H.263 sites a
/// chrominance sample at the centre of the four luminance samples it covers (clause 4.1, Figure 2),
/// so bringing the planes back up is an interpolation between neighbours rather than a repetition of
/// each sample across a two-by-two square: repeating leaves a visible step at every second column,
/// worst exactly where colour changes fastest.
/// <para/>
/// <b>That second choice is where this and ffmpeg part company, and it is worth knowing before
/// comparing pictures.</b> ffmpeg's <c>yuv420p</c> to <c>rgb24</c> path repeats each chrominance
/// sample across its square, so on a picture of hard colour edges — a test pattern of colour bars,
/// say — nearly half the samples of every frame come out tens of levels apart from these, while the
/// decoded 4:2:0 samples the two are converting are identical. The difference can be measured without
/// a decoder at all: ffmpeg's own decoded planes put through the conversion here differ from ffmpeg's
/// own RGB by the same amount. A comparison that means anything about the decode is therefore made on
/// the sample planes, or on a stream whose chrominance is constant.
/// </remarks>
internal static class H263ColorConversion {

  /// <summary>Crops a decoded picture to its displayed size and converts it to packed 8-bit RGB.</summary>
  /// <param name="frame">The reconstructed planes, which are a whole number of macroblocks across.</param>
  /// <param name="width">The displayed width from the picture header.</param>
  /// <param name="height">The displayed height.</param>
  internal static byte[] ToRgb24(H263Frame frame, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * frame.LumaWidth;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = frame.Luma[lumaRow + x];
        var blue = _Chroma(frame.Cb, frame.ChromaWidth, frame.ChromaHeight, x, y) - 128;
        var red = _Chroma(frame.Cr, frame.ChromaWidth, frame.ChromaHeight, x, y) - 128;

        // ITU-R BT.601 with studio swing, in 8-bit fixed point:
        // 1.164 = 298/256, 1.596 = 409/256, 0.391 = 100/256, 0.813 = 208/256, 2.017 = 516/256.
        var scaledLuma = 298 * (luma - 16);

        rgb[target] = _Clamp(scaledLuma + 409 * red + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blue - 208 * red + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blue + 128);
        target += 3;
      }
    }

    return rgb;
  }

  /// <summary>
  /// One chrominance sample at a luminance position, interpolated from the four around it.
  /// </summary>
  /// <remarks>
  /// A luminance sample sits a quarter of a chrominance step from the chrominance sample covering it
  /// and three quarters from the next one along, so three parts of the near sample to one of the far
  /// one is that distance written as weights. Past the edge the near sample is used for both, which
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
