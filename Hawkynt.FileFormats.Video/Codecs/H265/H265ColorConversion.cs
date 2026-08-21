using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Turns a decoded 4:2:0 picture into the packed RGB every reader in this library hands back.
/// </summary>
/// <remarks>
/// Both steps here are display conventions rather than parts of the coding standard. H.265 codes
/// samples and describes, in an optional part of the sequence parameter set nothing is obliged to
/// send, what colour primaries and transfer they were meant for; what to do with them is the
/// display's. So a decoder that hands back RGB has had to choose, and the choices below are the ones
/// every player makes.
/// <para/>
/// Luminance runs 16 to 235 and chrominance 16 to 240 rather than filling the byte, so the conversion
/// expands that range — reading the samples as though they filled it leaves every picture washed out
/// by about seven per cent of its contrast, which looks like a decode that worked.
/// <para/>
/// The chrominance planes are half size, and where their samples sit relative to the luminance ones
/// is inherited from MPEG-2: level with the even luminance column and halfway between the two
/// luminance rows. So bringing chrominance back up is an exact copy on even columns and a halfway
/// average on odd ones, while vertically it is the three-to-one interpolation a quarter-step offset
/// calls for. Using centre siting instead shifts every colour edge half a luminance sample to the
/// left, which is small, everywhere, and looks like a decode that worked.
/// </remarks>
internal static class H265ColorConversion {

  /// <summary>Crops a decoded picture to its displayed size and converts it to packed 8-bit RGB.</summary>
  /// <param name="picture">The reconstructed planes, which may be larger than the displayed picture.</param>
  /// <param name="left">The first displayed column, from the conformance window.</param>
  /// <param name="top">The first displayed row.</param>
  /// <param name="width">The displayed width.</param>
  /// <param name="height">The displayed height.</param>
  internal static byte[] ToRgb24(H265Picture picture, int left, int top, int width, int height) {
    var rgb = new byte[width * height * 3];

    // The chrominance samples that belong to the displayed picture, which is not the whole plane. A
    // cropped stream is coded wider or taller than it is shown, and the samples past the crop are
    // real reconstructed samples that a later picture may predict from — but they are not part of
    // this picture, so the interpolation replicates the last displayed sample rather than reaching
    // into them.
    var chromaLeft = left >> 1;
    var chromaTop = top >> 1;
    var chromaRight = chromaLeft + ((width + 1) >> 1) - 1;
    var chromaBottom = chromaTop + ((height + 1) >> 1) - 1;

    for (var y = 0; y < height; ++y) {
      var lumaRow = (top + y) * picture.Width + left;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = picture.Luma[lumaRow + x];
        var cb = _Chroma(picture.Cb, picture.ChromaWidth, left + x, top + y,
          chromaLeft, chromaRight, chromaTop, chromaBottom);
        var cr = _Chroma(picture.Cr, picture.ChromaWidth, left + x, top + y,
          chromaLeft, chromaRight, chromaTop, chromaBottom);

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

  /// <summary>One chrominance sample at a luminance position, interpolated from the four around it.</summary>
  private static int _Chroma(
    byte[] plane, int planeWidth, int x, int y, int minX, int maxX, int minY, int maxY) {
    var nearX = x >> 1;
    var nearY = y >> 1;

    // Horizontally the sample is co-sited with the even column, so an even column reads it whole and
    // an odd one splits evenly between it and the next.
    var farX = (x & 1) == 0 ? nearX : _Clip(nearX + 1, minX, maxX);
    var nearWeightX = (x & 1) == 0 ? 2 : 1;
    var farWeightX = 2 - nearWeightX;

    // Vertically it sits halfway between the two rows it covers, so each row is a quarter step away
    // from it in one direction or the other: three parts of the near sample to one of the far.
    var farY = _Clip((y & 1) == 0 ? nearY - 1 : nearY + 1, minY, maxY);

    var topLeft = plane[nearY * planeWidth + nearX];
    var topRight = plane[nearY * planeWidth + farX];
    var bottomLeft = plane[farY * planeWidth + nearX];
    var bottomRight = plane[farY * planeWidth + farX];

    return (3 * (nearWeightX * topLeft + farWeightX * topRight)
            + (nearWeightX * bottomLeft + farWeightX * bottomRight)
            + 4) >> 3;
  }

  private static int _Clip(int value, int lowest, int highest)
    => value < lowest ? lowest : value > highest ? highest : value;

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
