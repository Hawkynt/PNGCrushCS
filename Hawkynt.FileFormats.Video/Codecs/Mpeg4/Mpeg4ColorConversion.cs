namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// Turns a decoded 4:2:0 picture into the packed RGB every reader in this library hands back.
/// </summary>
/// <remarks>
/// Two steps, and both of them are conventions rather than parts of the coding standard. ISO/IEC
/// 14496-2 codes samples and says what colour primaries they are meant for; it is the display that
/// decides what to do with them, so a decoder that hands back RGB has had to choose.
/// <para/>
/// Luminance runs 16 to 235 and chrominance 16 to 240 rather than filling the byte, so the conversion
/// expands that range — reading the samples as though they filled it leaves every picture washed out
/// by about seven per cent of its contrast, which looks like a decode that worked.
/// <para/>
/// The chrominance planes are half size, and where their samples sit is where MPEG-4 Part 2 parts
/// company with the MPEG-1 decoder beside it. MPEG-4 sites a chrominance sample on the even luminance
/// column and halfway between the two luminance rows it covers, so bringing the planes back up is an
/// exact copy at even columns and a halving at odd ones, with three parts of the nearer row to one of
/// the further in the other direction. Using MPEG-1's siting instead — centred both ways — shifts
/// every colour half a luminance sample to the left, which is a tint along one edge of every coloured
/// object and not along the other.
/// <para/>
/// <b>Both choices differ from the reference decoder's, and that is worth knowing before comparing
/// pictures.</b> ffmpeg's <c>yuv420p</c> to <c>rgb24</c> path repeats each chrominance sample across
/// its two-by-two square rather than interpolating, so on a picture of hard colour edges nearly half
/// the samples of every frame come out tens of levels apart from these — while the decoded 4:2:0
/// samples the two are converting are identical. The difference can be measured without a decoder at
/// all: ffmpeg's own decoded planes put through the conversion here differ from ffmpeg's own RGB by
/// the same amount. A comparison that says anything about the decode is therefore made on the sample
/// planes, or on a stream whose chrominance is constant.
/// </remarks>
internal static class Mpeg4ColorConversion {

  /// <summary>Crops a decoded picture to its displayed size and converts it to packed 8-bit RGB.</summary>
  /// <param name="frame">The reconstructed planes, which are a whole number of macroblocks across.</param>
  /// <param name="width">The displayed width from the video object layer.</param>
  /// <param name="height">The displayed height.</param>
  internal static byte[] ToRgb24(Mpeg4Frame frame, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var lumaRow = frame.LumaOrigin + y * frame.LumaStride;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = frame.Luma[lumaRow + x];
        var blue = _Chroma(frame, frame.Cb, x, y) - 128;
        var red = _Chroma(frame, frame.Cr, x, y) - 128;

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
  /// The weights are the two directions multiplied together over a denominator of eight: two parts to
  /// nought horizontally on an even column and one to one on an odd one, three parts to one
  /// vertically either way. Doing both directions in one sum rather than one after the other avoids
  /// rounding twice, which would cost about half a level on every second sample.
  /// <para/>
  /// The plane is padded, so the sample past the right edge and the row past the bottom are the edge
  /// repeated and no clamping is needed here.
  /// </remarks>
  private static int _Chroma(Mpeg4Frame frame, byte[] plane, int x, int y) {
    var stride = frame.ChromaStride;
    var nearX = x >> 1;
    var nearY = y >> 1;

    // Vertically the sample sits halfway between the two luminance rows it covers, so an even
    // luminance row leans towards the chrominance row above and an odd one towards the row below.
    var farY = (y & 1) == 0 ? nearY - 1 : nearY + 1;

    var near = frame.ChromaOrigin + nearY * stride + nearX;
    var far = frame.ChromaOrigin + farY * stride + nearX;

    if ((x & 1) == 0)
      return (6 * plane[near] + 2 * plane[far] + 4) >> 3;

    return (3 * plane[near] + 3 * plane[near + 1] + plane[far] + plane[far + 1] + 4) >> 3;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
