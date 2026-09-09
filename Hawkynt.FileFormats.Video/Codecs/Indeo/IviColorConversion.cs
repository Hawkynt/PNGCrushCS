namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Turns a decoded Indeo picture into packed RGB.
/// </summary>
/// <remarks>
/// Neither format says anything about what its samples are meant for; a reader that hands back RGB
/// has had to choose, and the choice here is the one the package's other decoders of this era make —
/// ITU-R BT.601 with studio swing, so luminance from 16 to 235 and chrominance from 16 to 240 fill
/// the byte rather than being read as though they already did.
/// <para/>
/// The chrominance planes are a quarter of the picture in each direction, so one chrominance sample
/// covers a four-by-four square of luminance samples, and this repeats it across that square rather
/// than interpolating. That is what Intel's own decoders did and what ffmpeg does, and at this
/// subsampling it matters: an interpolation over sixteen pixels invents a gradient across every
/// colour edge in the picture, which is a much larger departure than the same choice makes at 4:2:0.
/// <para/>
/// The sample planes and not this are what the decode is measured on. The planes are what the
/// bitstream defines; everything here is a display convention that the format never fixed.
/// </remarks>
internal static class IviColorConversion {

  internal static byte[] ToRgb24(IviPicture picture) {
    var width = picture.Width;
    var height = picture.Height;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * width;
      var chromaRow = (y >> 2) * picture.ChromaWidth;
      var target = lumaRow * 3;

      for (var x = 0; x < width; ++x) {
        var chromaAt = chromaRow + (x >> 2);
        var blueDifference = picture.ChromaBlue[chromaAt] - 128;
        var redDifference = picture.ChromaRed[chromaAt] - 128;

        // ITU-R BT.601 with studio swing, in 8-bit fixed point:
        // 1.164 = 298/256, 1.596 = 409/256, 0.391 = 100/256, 0.813 = 208/256, 2.017 = 516/256.
        var scaledLuma = 298 * (picture.Luma[lumaRow + x] - 16);

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
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
