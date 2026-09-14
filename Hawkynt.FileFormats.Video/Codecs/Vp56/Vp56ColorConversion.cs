namespace FileFormat.Codecs.Vp56;

/// <summary>Turns decoded VP5/VP6 4:2:0 planes into the library's packed RGB frame representation.</summary>
internal static class Vp56ColorConversion {
  internal static byte[] ToRgb24(Vp56Frame frame, int width, int height, bool flipVertically) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var codedY = flipVertically ? frame.LumaHeight - 1 - y : y;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = frame.Luma[codedY * frame.LumaWidth + x];
        var blueDifference = _Chroma(frame.Cb, frame.ChromaWidth, frame.ChromaHeight, x, codedY) - 128;
        var redDifference = _Chroma(frame.Cr, frame.ChromaWidth, frame.ChromaHeight, x, codedY) - 128;
        var scaledLuma = 298 * (luma - 16);

        rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        target += 3;
      }
    }

    return rgb;
  }

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
