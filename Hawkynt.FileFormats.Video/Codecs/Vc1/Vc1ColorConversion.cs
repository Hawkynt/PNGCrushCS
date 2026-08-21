namespace FileFormat.Codecs.Vc1;

/// <summary>
/// Turns a decoded 4:2:0 picture into the packed RGB every reader in this library hands back.
/// </summary>
/// <remarks>
/// Both steps are conventions rather than parts of the coding standard. SMPTE 421M codes samples and
/// says what they are meant for; it is the display that decides what to do with them, and a decoder
/// that hands back RGB has had to choose. The choices here are the ones the other decoders in this
/// library make, so that a frame of Windows Media Video and a frame of MPEG-1 come out of the same
/// arithmetic.
/// <para/>
/// <b>The second choice is where this and ffmpeg part company, and it matters before comparing
/// pictures.</b> ffmpeg's <c>yuv420p</c> to <c>rgb24</c> path repeats each chrominance sample across
/// its two-by-two square where this interpolates between neighbours, so on a picture with hard colour
/// edges nearly half the samples of every frame come out tens of levels apart even when the decoded
/// 4:2:0 samples the two are converting are identical. A comparison that means anything about the
/// decode is therefore made on the sample planes.
/// </remarks>
internal static class Vc1ColorConversion {

  /// <summary>Crops a decoded picture to its displayed size and converts it to packed 8-bit RGB.</summary>
  /// <param name="frame">The reconstructed planes, which are a whole number of macroblocks across.</param>
  /// <param name="width">The displayed width the container stated.</param>
  /// <param name="height">The displayed height.</param>
  internal static byte[] ToRgb24(Vc1Frame frame, int width, int height) {
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

        rgb[target] = _Clamp(scaledLuma + (409 * red) + 128);
        rgb[target + 1] = _Clamp(scaledLuma - (100 * blue) - (208 * red) + 128);
        rgb[target + 2] = _Clamp(scaledLuma + (516 * blue) + 128);
        target += 3;
      }
    }

    return rgb;
  }

  /// <summary>One chrominance sample at a luminance position, interpolated from the four around it.</summary>
  private static int _Chroma(int[] plane, int planeWidth, int planeHeight, int x, int y) {
    var nearX = x >> 1;
    var nearY = y >> 1;
    var farX = _Neighbour(nearX, x, planeWidth);
    var farY = _Neighbour(nearY, y, planeHeight);

    var topLeft = plane[(nearY * planeWidth) + nearX];
    var topRight = plane[(nearY * planeWidth) + farX];
    var bottomLeft = plane[(farY * planeWidth) + nearX];
    var bottomRight = plane[(farY * planeWidth) + farX];

    // 9:3:3:1 over the four, which is the separable form of three-to-one in each direction.
    return ((9 * topLeft) + (3 * topRight) + (3 * bottomLeft) + bottomRight + 8) >> 4;
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
